// This file is part of the ArmoniK project
// 
// Copyright (C) ANEO, 2022-2026. All rights reserved.
// 
// Licensed under the Apache License, Version 2.0 (the "License")
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
//     http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace ArmoniK.Utils;

internal static class Rechunker
{
  // Implementation of the Rechunk and ToChunksAsync functions
  // Input chunks within bounds are yielded as is, and oversized input chunks are sliced, without any copy.
  // Only fragments smaller than minSize are copied into a buffer, in order to be merged with the next input chunks.
  // Arrays yielded from the buffer always have the exact size of the chunk.
  // No reference to an input chunk is kept across a MoveNextAsync of the source: the source can reuse its memory.
  //
  // The buffered data is yielded early when its deadline expires (time of arrival of its oldest element + maxDelay),
  // or when the flusher is triggered. In both cases, the iterator stops waiting for the source, but data that is
  // already available is still merged.
  internal static async IAsyncEnumerable<TOut> IteratorAsync<TIn, T, TOut, TAdapter>(IAsyncEnumerable<TIn>                      source,
                                                                                     TAdapter                                   adapter,
                                                                                     int                                        minSize,
                                                                                     int                                        maxSize,
                                                                                     TimeSpan                                   maxDelay,
                                                                                     ChunkFlusher?                              flusher,
                                                                                     [EnumeratorCancellation] CancellationToken cancellationToken = default)
    where TAdapter : struct, IAdapter<TIn, T, TOut>
  {
    var hasDeadline = maxDelay != Timeout.InfiniteTimeSpan;
    var timed       = hasDeadline || flusher is not null;
    var delayMs = hasDeadline
                    ? (int)Math.Ceiling(maxDelay.TotalMilliseconds)
                    : 0;

    var buffer = new Accumulator<T>(minSize);

    // State of the data currently buffered, only meaningful while the buffer is not empty
    var                      bufferStart = 0; // Environment.TickCount at the arrival of the oldest element
    var                      flushToken  = default(CancellationToken);
    CancellationTokenSource? triggerCts  = null; // Cancelled on deadline or flush, created on first wait
    Task?                    trigger     = null;
    Task[]?                  waitTasks   = null; // Reused for each wait on the source and the trigger

    cancellationToken.ThrowIfCancellationRequested();

    // When timed, a chunk can be yielded while the source is still fetching its next element:
    // the iterator must be able to cancel this fetch if the enumeration stops
    using var sourceCts = timed
                            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
                            : null;
    var        enumerator = source.GetAsyncEnumerator(sourceCts?.Token ?? cancellationToken);
    var        next       = default(ValueTask<bool>);
    var        inFlight   = false;
    Exception? error      = null;

    try
    {
      while (true)
      {
        if (!inFlight)
        {
          try
          {
            cancellationToken.ThrowIfCancellationRequested();
            next     = enumerator.MoveNextAsync();
            inFlight = true;
          }
          catch (Exception e)
          {
            error = e;
            break;
          }
        }

        // The source is not ready and data is buffered: wait for the source, the deadline, or the flusher
        if (timed && !buffer.IsEmpty && !next.IsCompleted)
        {
          var remaining = delayMs - unchecked(Environment.TickCount - bufferStart);
          var flush     = flushToken.IsCancellationRequested || (hasDeadline && remaining <= 0);

          if (!flush)
          {
            if (trigger is null)
            {
              triggerCts = flushToken.CanBeCanceled
                             ? CancellationTokenSource.CreateLinkedTokenSource(flushToken)
                             : new CancellationTokenSource();
              if (hasDeadline)
              {
                triggerCts.CancelAfter(remaining);
              }

              // Continuations must not run synchronously on the thread calling Flush
              var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
              triggerCts.Token.Register(static state => ((TaskCompletionSource<bool>)state!).TrySetResult(true),
                                        tcs);
              trigger = tcs.Task;
            }

            var nextTask = next.AsTask();
            next = new ValueTask<bool>(nextTask);

            // netstandard2.0 only has WhenAny(params Task[]): reuse the array instead of allocating one per wait.
            // It is safe as the array is only modified once WhenAny has completed.
            waitTasks    ??= new Task[2];
            waitTasks[0] =   nextTask;
            waitTasks[1] =   trigger;
            var winner = await Task.WhenAny(waitTasks)
                                   .ConfigureAwait(false);
            waitTasks[0] = waitTasks[1] = null!;
            flush        = winner != nextTask;
          }

          if (flush)
          {
            var chunk = buffer.TakeExact();
            EndBuffering(ref triggerCts,
                         ref trigger,
                         ref flushToken);

            // The source is still fetching: next stays in flight
            yield return adapter.FromMemory(chunk);

            // Nothing is buffered anymore: cancellation can be reported without waiting for the source
            try
            {
              cancellationToken.ThrowIfCancellationRequested();
            }
            catch (Exception e)
            {
              error = e;
              break;
            }

            continue;
          }
        }

        bool hasNext;
        try
        {
          hasNext = await next.ConfigureAwait(false);
        }
        catch (Exception e)
        {
          error = e;
          break;
        }
        finally
        {
          inFlight = false;
        }

        if (!hasNext)
        {
          break;
        }

        var pending = adapter.ToMemory(enumerator.Current);
        var arrival = hasDeadline
                        ? Environment.TickCount
                        : 0;

        // Buffer is not empty: complete it
        if (!buffer.IsEmpty && !pending.IsEmpty)
        {
          var missing = minSize - buffer.Count;
          if (pending.Length < missing)
          {
            buffer.Append(pending.Span);
            pending = default;
          }
          else
          {
            // If what remains after reaching minSize cannot be yielded on its own, it would have to be copied anyway:
            // absorb as much as possible into this chunk instead
            var take = pending.Length - missing >= minSize
                         ? missing
                         : Math.Min(pending.Length,
                                    maxSize - buffer.Count);

            var merged = buffer.TakeMerged(pending.Span.Slice(0,
                                                              take));
            pending = pending.Slice(take);
            EndBuffering(ref triggerCts,
                         ref trigger,
                         ref flushToken);

            yield return adapter.FromMemory(merged);
          }
        }

        // Buffer is empty: slice what is large enough without copy
        while (pending.Length >= minSize)
        {
          var size = SliceSize(pending.Length,
                               minSize,
                               maxSize);
          var slice = pending.Slice(0,
                                    size);
          pending = pending.Slice(size);

          yield return adapter.FromMemory(slice);
        }

        // Fragment too small: it must be merged with the next input chunks
        if (!pending.IsEmpty)
        {
          // Deadline starts at the arrival of the input chunk, not when its remainder is buffered
          bufferStart = arrival;
          flushToken  = flusher?.Token ?? default;
          buffer.Append(pending.Span);
        }

        // The source may be slow even though it completes synchronously: check the deadline and the flusher
        if (timed && !buffer.IsEmpty && (flushToken.IsCancellationRequested || (hasDeadline && delayMs - unchecked(Environment.TickCount - bufferStart) <= 0)))
        {
          var chunk = buffer.TakeExact();
          EndBuffering(ref triggerCts,
                       ref trigger,
                       ref flushToken);

          yield return adapter.FromMemory(chunk);
        }
      }

      // Last chunk can be smaller than minSize, and must be yielded even if there is an error
      if (!buffer.IsEmpty)
      {
        yield return adapter.FromMemory(buffer.TakeExact());
      }
    }
    finally
    {
      triggerCts?.Dispose();

      // The source cannot be disposed while it is still fetching
      if (inFlight)
      {
        sourceCts?.Cancel();
        try
        {
          await next.ConfigureAwait(false);
        }
        catch
        {
          // The enumeration is over: the result of the fetch is irrelevant
        }
      }

      await enumerator.DisposeAsync()
                      .ConfigureAwait(false);
    }

    error?.RethrowWithStacktrace();
  }

  // Validate maxDelay, and normalize "no timeout" to Timeout.InfiniteTimeSpan
  internal static TimeSpan ValidateMaxDelay(TimeSpan? maxDelay)
  {
    var delay = maxDelay ?? Timeout.InfiniteTimeSpan;
    if (delay != Timeout.InfiniteTimeSpan && (delay < TimeSpan.Zero || delay.TotalMilliseconds > int.MaxValue))
    {
      throw new ArgumentOutOfRangeException(nameof(maxDelay),
                                            maxDelay,
                                            "Maximum delay must be infinite, or between 0 and int.MaxValue milliseconds");
    }

    return delay;
  }

  private static void EndBuffering(ref CancellationTokenSource? triggerCts,
                                   ref Task?                    trigger,
                                   ref CancellationToken        flushToken)
  {
    triggerCts?.Dispose();
    triggerCts = null;
    trigger    = null;
    flushToken = default;
  }

  // Size of the next slice of a chunk of the given length, with length >= minSize
  private static int SliceSize(int length,
                               int minSize,
                               int maxSize)
  {
    if (length <= maxSize)
    {
      return length;
    }

    if (length - maxSize >= minSize)
    {
      return maxSize;
    }

    if (length - minSize >= minSize)
    {
      // Leave exactly minSize elements so that the remainder can also be yielded without copy
      return length - minSize;
    }

    // Cannot be split in two valid chunks: minimize the remainder that will be copied
    return maxSize;
  }

  /// <summary>
  ///   Conversion between the items of the source, the chunks handled by the rechunker, and the yielded chunks.
  /// </summary>
  /// <remarks>
  ///   Implemented by structs so that the conversions are specialized and inlined by the JIT.
  ///   An adapter is copied for each enumeration, so it can hold per-enumeration state.
  /// </remarks>
  internal interface IAdapter<in TIn, T, out TOut>
  {
    ReadOnlyMemory<T> ToMemory(TIn item);

    TOut FromMemory(ReadOnlyMemory<T> chunk);
  }

  internal struct MemoryAdapter<T> : IAdapter<ReadOnlyMemory<T>, T, ReadOnlyMemory<T>>
  {
    public ReadOnlyMemory<T> ToMemory(ReadOnlyMemory<T> item)
      => item;

    public ReadOnlyMemory<T> FromMemory(ReadOnlyMemory<T> chunk)
      => chunk;
  }

  /// <summary>
  ///   Buffer accumulating fragments smaller than minSize.
  ///   Its capacity grows progressively up to minSize, so that memory stays proportional to the buffered data.
  /// </summary>
  private struct Accumulator<T>
  {
    private readonly int  minSize_;
    private          T[]? buffer_;

    // Capacity of the last buffer handed out, used to size the next one and avoid regrowing it from scratch
    private int capacityHint_;

    public Accumulator(int minSize)
    {
      minSize_      = minSize;
      buffer_       = null;
      capacityHint_ = 0;
      Count         = 0;
    }

    public int Count { get; private set; }

    public bool IsEmpty
      => Count == 0;

    /// <summary>
    ///   Append <paramref name="items" />. The total must not exceed minSize.
    /// </summary>
    public void Append(ReadOnlySpan<T> items)
    {
      var required = Count + items.Length;
      if (buffer_ is null || buffer_.Length < required)
      {
        var length = buffer_?.Length ?? 0;
        var capacity = (int)Math.Min(Math.Max(Math.Max((long)length * 3 / 2,
                                                       4),
                                              Math.Max(required,
                                                       capacityHint_)),
                                     minSize_);
        var array = new T[capacity];
        buffer_?.AsSpan(0,
                        Count)
               .CopyTo(array);
        buffer_ = array;
      }

      items.CopyTo(buffer_.AsSpan(Count));
      Count = required;
    }

    /// <summary>
    ///   Take the buffered data, merged with <paramref name="items" />, as an array of the exact size.
    /// </summary>
    public ReadOnlyMemory<T> TakeMerged(ReadOnlySpan<T> items)
    {
      if (Count + items.Length <= minSize_)
      {
        Append(items);
        return TakeExact();
      }

      // Larger than the buffer can be: allocate the chunk with its exact size, and keep the buffer for later
      var array = new T[Count + items.Length];
      buffer_.AsSpan(0,
                     Count)
             .CopyTo(array);
      items.CopyTo(array.AsSpan(Count));
      Count = 0;
      return array;
    }

    /// <summary>
    ///   Take the buffered data as an array of the exact size.
    /// </summary>
    public ReadOnlyMemory<T> TakeExact()
    {
      var count = Count;
      Count = 0;

      if (count == buffer_!.Length)
      {
        // Buffer is full: hand it out, and allocate a fresh one for the next chunk
        var full = buffer_;
        buffer_       = null;
        capacityHint_ = count;
        return full;
      }

      // Copy only the valid part, and keep the buffer for later
      return buffer_.AsSpan(0,
                            count)
                    .ToArray();
    }
  }
}

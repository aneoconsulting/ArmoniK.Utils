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
  // Implementation of the Rechunk and ToChunksAsync functions.
  //
  // The splitting and merging of chunks is done by Chunker, and the decision to yield buffered data early
  // (deadline expired, or flusher triggered) by FlushTrigger. This iterator only drives them:
  //   - fetch the next input chunk from the source,
  //   - if data is buffered and the source is not ready, wait for the source or the trigger, whichever comes first,
  //   - give the input chunk to the chunker and yield the chunks it produces.
  //
  // When the trigger fires while waiting, the buffered data is yielded while the source is still fetching.
  // That fetch must then be awaited (and cancelled if the enumeration stops) before the source is disposed.
  // Errors, including cancellation, are reported once all the data received so far has been yielded.
  internal static async IAsyncEnumerable<TOut> IteratorAsync<TIn, T, TOut, TAdapter>(IAsyncEnumerable<TIn>                      source,
                                                                                     TAdapter                                   adapter,
                                                                                     int                                        minSize,
                                                                                     int                                        maxSize,
                                                                                     TimeSpan                                   maxDelay,
                                                                                     ChunkFlusher?                              flusher,
                                                                                     [EnumeratorCancellation] CancellationToken cancellationToken = default)
    where TAdapter : struct, IAdapter<TIn, T, TOut>
  {
    var chunker = new Chunker<T>(minSize,
                                 maxSize);
    using var trigger = maxDelay == Timeout.InfiniteTimeSpan && flusher is null
                          ? null
                          : new FlushTrigger(maxDelay,
                                             flusher);

    // Only needed to cancel a fetch still in flight, which can only happen with a trigger
    using var sourceCts = trigger is null
                            ? null
                            : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    var        enumerator = source.GetAsyncEnumerator(sourceCts?.Token ?? cancellationToken);
    var        next       = default(ValueTask<bool>);
    var        inFlight   = false;
    Exception? error      = null;

    try
    {
      while (true)
      {
        try
        {
          cancellationToken.ThrowIfCancellationRequested();
          if (!inFlight)
          {
            next     = enumerator.MoveNextAsync();
            inFlight = true;
          }
        }
        catch (Exception e)
        {
          error = e;
          break;
        }

        // Data is buffered but the source is not ready: do not wait for it beyond the trigger
        if (trigger is
            {
              IsArmed: true,
            } && !next.IsCompleted)
        {
          var due = trigger.IsDue;
          if (!due)
          {
            var nextTask = next.AsTask();
            next = new ValueTask<bool>(nextTask);
            due = await trigger.WhenAny(nextTask)
                               .ConfigureAwait(false) != nextTask;
          }

          if (due)
          {
            trigger.Disarm();
            yield return adapter.FromMemory(chunker.Flush());
            continue; // The source is still fetching
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

        // The deadline of buffered data starts when it is received, not when it is buffered
        var arrival = Environment.TickCount;

        chunker.Push(adapter.ToMemory(enumerator.Current));
        while (chunker.TryPop(out var chunk))
        {
          // A chunk is only produced once the buffer is empty
          trigger?.Disarm();
          yield return adapter.FromMemory(chunk);
        }

        if (trigger is not null && !chunker.IsEmpty)
        {
          if (!trigger.IsArmed)
          {
            trigger.Arm(arrival);
          }

          // The source may be slow even though it completes synchronously
          if (trigger.IsDue)
          {
            trigger.Disarm();
            yield return adapter.FromMemory(chunker.Flush());
          }
        }
      }

      // Last chunk can be smaller than minSize, and must be yielded even if there is an error
      if (!chunker.IsEmpty)
      {
        yield return adapter.FromMemory(chunker.Flush());
      }
    }
    finally
    {
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
  ///   Splits and merges input chunks into chunks between minSize and maxSize.
  /// </summary>
  /// <remarks>
  ///   <para>
  ///     Input chunks within bounds are produced as is, and oversized input chunks are sliced, without any copy.
  ///     Only fragments smaller than minSize are copied into a buffer, to be merged with the next input chunks.
  ///     Chunks produced from the buffer are always arrays of the exact size of the chunk.
  ///   </para>
  ///   <para>
  ///     Once <see cref="TryPop" /> returns false, the input chunk has been entirely consumed and is not referenced
  ///     anymore: the source is free to reuse its memory.
  ///   </para>
  /// </remarks>
  private sealed class Chunker<T>
  {
    private readonly int maxSize_;
    private readonly int minSize_;

    // Fragments waiting to reach minSize. The capacity grows progressively up to minSize, so that memory stays
    // proportional to the buffered data. Only the first count_ elements are valid, and count_ < minSize_.
    private T[]? buffer_;
    private int  count_;
    private int  nextCapacity_;

    // Part of the current input chunk that has not been processed yet
    private ReadOnlyMemory<T> pending_;

    public Chunker(int minSize,
                   int maxSize)
    {
      minSize_ = minSize;
      maxSize_ = maxSize;
    }

    public bool IsEmpty
      => count_ == 0;

    /// <summary>
    ///   Give the next input chunk. The previous one must have been entirely consumed.
    /// </summary>
    public void Push(ReadOnlyMemory<T> input)
      => pending_ = input;

    /// <summary>
    ///   Produce the next chunk from the current input chunk.
    ///   The buffer is always empty when a chunk is produced.
    /// </summary>
    /// <returns>Whether a chunk has been produced, false when the input chunk has been entirely consumed</returns>
    public bool TryPop(out ReadOnlyMemory<T> chunk)
    {
      chunk = default;
      var length = pending_.Length;
      if (length == 0)
      {
        return false;
      }

      // Not enough data for a chunk: buffer it until the next input chunk
      if (count_ + length < minSize_)
      {
        Append(pending_.Span);
        pending_ = default;
        return false;
      }

      int size;
      if (count_ > 0)
      {
        // Complete the buffer. If what remains after reaching minSize could not be produced on its own, it would have
        // to be copied anyway: absorb as much as possible into this chunk instead.
        var missing = minSize_ - count_;
        size = length - missing >= minSize_
                 ? missing
                 : Math.Min(length,
                            maxSize_ - count_);
        chunk = Merge(pending_.Span.Slice(0,
                                          size));
      }
      else
      {
        // Large enough: slice it without copy
        size = SliceSize(length);
        chunk = pending_.Slice(0,
                               size);
      }

      pending_ = pending_.Slice(size);
      return true;
    }

    /// <summary>
    ///   Take the buffered data, as an array of the exact size.
    /// </summary>
    public ReadOnlyMemory<T> Flush()
    {
      var count = count_;
      count_ = 0;

      if (count == buffer_!.Length)
      {
        // Buffer is full: hand it out, and allocate a fresh one of the same size for the next chunk
        var full = buffer_;
        buffer_       = null;
        nextCapacity_ = count;
        return full;
      }

      // Copy only the valid part, and keep the buffer for later
      return buffer_.AsSpan(0,
                            count)
                    .ToArray();
    }

    // Take the buffered data merged with items, as an array of the exact size
    private ReadOnlyMemory<T> Merge(ReadOnlySpan<T> items)
    {
      if (count_ + items.Length == minSize_)
      {
        Append(items);
        return Flush();
      }

      // Larger than the buffer can be: allocate the chunk with its exact size, and keep the buffer for later
      var array = new T[count_ + items.Length];
      buffer_.AsSpan(0,
                     count_)
             .CopyTo(array);
      items.CopyTo(array.AsSpan(count_));
      count_ = 0;
      return array;
    }

    // Append items to the buffer. The total must not exceed minSize.
    private void Append(ReadOnlySpan<T> items)
    {
      var required = count_ + items.Length;
      if (buffer_ is null || buffer_.Length < required)
      {
        // Grow by 1.5x (at least 4), at least to what is required, and at most to minSize
        var length = buffer_?.Length ?? 0;
        var capacity = Math.Max(4L,
                                length * 3L / 2);
        capacity = Math.Max(capacity,
                            Math.Max(required,
                                     nextCapacity_));
        capacity = Math.Min(capacity,
                            minSize_);

        var array = new T[capacity];
        buffer_?.AsSpan(0,
                        count_)
               .CopyTo(array);
        buffer_ = array;
      }

      items.CopyTo(buffer_.AsSpan(count_));
      count_ = required;
    }

    // Size of the next slice of a chunk of the given length, with length >= minSize
    private int SliceSize(int length)
    {
      if (length <= maxSize_)
      {
        return length;
      }

      if (length - maxSize_ >= minSize_)
      {
        return maxSize_;
      }

      if (length - minSize_ >= minSize_)
      {
        // Leave exactly minSize elements so that the remainder can also be produced without copy
        return length - minSize_;
      }

      // Cannot be split in two valid chunks: minimize the remainder that will be copied
      return maxSize_;
    }
  }

  /// <summary>
  ///   Decides when buffered data must be yielded early: when its deadline expires (time of arrival of its oldest
  ///   element + maxDelay), or when the flusher is triggered.
  /// </summary>
  /// <remarks>
  ///   The trigger is armed while data is buffered. A flush requested while it is not armed is ignored.
  /// </remarks>
  private sealed class FlushTrigger : IDisposable
  {
    private readonly int           delayMs_; // Timeout.Infinite if there is no deadline
    private readonly ChunkFlusher? flusher_;

    // netstandard2.0 only has WhenAny(params Task[]): the array is reused instead of being allocated for each wait.
    // It is safe as WhenAny is only called again once the previous one has completed.
    private readonly Task?[] waitTasks_ = new Task?[2];

    private CancellationToken        flushToken_;
    private int                      start_;
    private CancellationTokenSource? taskCts_;
    private Task?                    task_; // Completed when due, created on the first wait

    public FlushTrigger(TimeSpan      maxDelay,
                        ChunkFlusher? flusher)
    {
      delayMs_ = maxDelay == Timeout.InfiniteTimeSpan
                   ? Timeout.Infinite
                   : (int)Math.Ceiling(maxDelay.TotalMilliseconds);
      flusher_ = flusher;
    }

    public bool IsArmed { get; private set; }

    public bool IsDue
      => IsArmed && (flushToken_.IsCancellationRequested || (delayMs_ != Timeout.Infinite && Remaining <= 0));

    private int Remaining
      => delayMs_ - unchecked(Environment.TickCount - start_);

    public void Dispose()
      => Disarm();

    /// <summary>
    ///   Start the deadline for data received at <paramref name="start" /> (<see cref="Environment.TickCount" />)
    /// </summary>
    public void Arm(int start)
    {
      IsArmed     = true;
      start_      = start;
      flushToken_ = flusher_?.Token ?? default;
    }

    public void Disarm()
    {
      if (!IsArmed)
      {
        return;
      }

      IsArmed     = false;
      flushToken_ = default;
      task_       = null;
      taskCts_?.Dispose();
      taskCts_      = null;
      waitTasks_[0] = waitTasks_[1] = null;
    }

    /// <summary>
    ///   Wait for <paramref name="next" />, or for the trigger to be due, whichever comes first.
    ///   Must only be called while armed and not due.
    /// </summary>
    /// <returns>The task that completed first</returns>
    public Task<Task> WhenAny(Task next)
    {
      if (task_ is null)
      {
        taskCts_ = flushToken_.CanBeCanceled
                     ? CancellationTokenSource.CreateLinkedTokenSource(flushToken_)
                     : new CancellationTokenSource();
        if (delayMs_ != Timeout.Infinite)
        {
          taskCts_.CancelAfter(Remaining);
        }

        // Continuations must not run synchronously on the thread calling Flush
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        taskCts_.Token.Register(static state => ((TaskCompletionSource<bool>)state!).TrySetResult(true),
                                tcs);
        task_ = tcs.Task;
      }

      waitTasks_[0] = next;
      waitTasks_[1] = task_;
      return Task.WhenAny(waitTasks_!);
    }
  }
}

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
using System.Threading.Tasks.Sources;

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

        // Data is buffered but the source is not ready: do not wait for it beyond the trigger.
        // Once the source has been waited for through the trigger, it must keep being waited for through it.
        if (trigger is not null && (trigger.IsHooked || (trigger.IsArmed && !next.IsCompleted)))
        {
          var due = !await trigger.WaitAsync(next)
                                  .ConfigureAwait(false);
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
          trigger?.Unhook();
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
          if (trigger is
              {
                IsHooked: true,
              })
          {
            trigger.Disarm();
            await trigger.WaitAsync(next)
                         .ConfigureAwait(false);
          }

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
  ///   It also waits for the source or the trigger, whichever comes first, without allocating.
  /// </summary>
  /// <remarks>
  ///   <para>
  ///     The trigger is armed while data is buffered. A flush requested while it is not armed is ignored.
  ///   </para>
  ///   <para>
  ///     To wait without allocating, a continuation is registered on the pending fetch of the source (it is then
  ///     "hooked"), and the trigger completes the same reusable <see cref="IValueTaskSource{TResult}" />.
  ///     As a fetch accepts only one continuation, a hooked fetch must be waited for through
  ///     <see cref="WaitAsync" /> until it completes, even if the trigger fired first.
  ///   </para>
  /// </remarks>
  private sealed class FlushTrigger : IValueTaskSource<bool>, IDisposable
  {
    // States of the fetch of the source
    private const int NotHooked  = 0;
    private const int Hooked     = 1;
    private const int Completing = 2;
    private const int Completed  = 3;

    // waiting_ is NotWaiting, WaitingForNext, or the period of the current wait when the trigger can complete it.
    // Periods are 64 bits so that they never wrap around: a stale signal can never match a later period.
    private const long NotWaiting     = 0;
    private const long WaitingForNext = -1;

    private readonly int           delayMs_; // Timeout.Infinite if there is no deadline
    private readonly ChunkFlusher? flusher_;
    private readonly Action        onNextCompleted_;

    private ManualResetValueTaskSourceCore<bool> core_; // Result: whether the fetch completed (false: trigger)

    private CancellationTokenSource? cts_;
    private long                     firedPeriod_; // Last period whose trigger fired
    private CancellationToken        flushToken_;
    private int                      nextState_;
    private long                     period_; // Identifies the current armed period, > 0
    private int                      start_;
    private bool                     started_; // Whether the timer and the flush are watched
    private long                     waiting_;

    public FlushTrigger(TimeSpan      maxDelay,
                        ChunkFlusher? flusher)
    {
      delayMs_ = maxDelay == Timeout.InfiniteTimeSpan
                   ? Timeout.Infinite
                   : (int)Math.Ceiling(maxDelay.TotalMilliseconds);
      flusher_         = flusher;
      onNextCompleted_ = OnNextCompleted;
    }

    public bool IsArmed { get; private set; }

    public bool IsHooked
      => nextState_ != NotHooked;

    public bool IsDue
      => IsArmed && (flushToken_.IsCancellationRequested || (delayMs_ != Timeout.Infinite && Remaining <= 0));

    private int Remaining
      => delayMs_ - unchecked(Environment.TickCount - start_);

    public void Dispose()
      => Disarm();

    public bool GetResult(short token)
      => core_.GetResult(token);

    public ValueTaskSourceStatus GetStatus(short token)
      => core_.GetStatus(token);

    public void OnCompleted(Action<object?>                 continuation,
                            object?                         state,
                            short                           token,
                            ValueTaskSourceOnCompletedFlags flags)
      => core_.OnCompleted(continuation,
                           state,
                           token,
                           flags);

    /// <summary>
    ///   Start the deadline for data received at <paramref name="start" /> (<see cref="Environment.TickCount" />)
    /// </summary>
    public void Arm(int start)
    {
      IsArmed     = true;
      start_      = start;
      flushToken_ = flusher_?.Token ?? default;
      period_++;
    }

    public void Disarm()
    {
      if (!IsArmed)
      {
        return;
      }

      IsArmed     = false;
      flushToken_ = default;
      if (!started_)
      {
        return;
      }

      started_ = false;
      cts_!.Dispose();
      cts_ = null;
    }

    /// <summary>
    ///   The completed fetch has been consumed: the next one can be hooked.
    /// </summary>
    public void Unhook()
      => nextState_ = NotHooked;

    /// <summary>
    ///   Wait for <paramref name="next" />, or for the trigger if armed, whichever comes first.
    /// </summary>
    /// <returns>Whether <paramref name="next" /> completed, false if the trigger fired first</returns>
    public ValueTask<bool> WaitAsync(ValueTask<bool> next)
    {
      core_.Reset();
      var wait = IsArmed
                   ? period_
                   : WaitingForNext;
      Interlocked.Exchange(ref waiting_,
                           wait);

      if (nextState_ == NotHooked)
      {
        nextState_ = Hooked;
        next.ConfigureAwait(false)
            .GetAwaiter()
            .UnsafeOnCompleted(onNextCompleted_);
      }
      else if (Volatile.Read(ref nextState_) != Hooked)
      {
        // The fetch completed before this wait: its callback may have missed it, so complete it here
        var spinner = new SpinWait();
        while (Volatile.Read(ref nextState_) != Completed)
        {
          spinner.SpinOnce();
        }

        TryComplete(wait,
                    true);
      }

      if (IsArmed)
      {
        // Already due, or the trigger fired before this wait. Checked after the fetch, so that available data wins.
        if (IsDue || Volatile.Read(ref firedPeriod_) == period_)
        {
          TryComplete(wait,
                      false);
        }
        else if (!started_)
        {
          Start();
        }
      }

      return new ValueTask<bool>(this,
                                 core_.Version);
    }

    // Watch the deadline and the flusher for the current period
    private void Start()
    {
      started_ = true;
      var signal = new PeriodSignal(this,
                                    period_);
      cts_ = flushToken_.CanBeCanceled
               ? CancellationTokenSource.CreateLinkedTokenSource(flushToken_)
               : new CancellationTokenSource();
      if (delayMs_ != Timeout.Infinite)
      {
        cts_.CancelAfter(Math.Max(Remaining,
                                  0));
      }

      cts_.Token.Register(PeriodSignal.OnCancel,
                          signal);
    }

    // Called once the fetch completes. Nothing is done after Completed, so that the fetch can be unhooked.
    private void OnNextCompleted()
    {
      Volatile.Write(ref nextState_,
                     Completing);
      var wait = Interlocked.Exchange(ref waiting_,
                                      NotWaiting);
      Volatile.Write(ref nextState_,
                     Completed);

      if (wait != NotWaiting)
      {
        core_.SetResult(true);
      }
    }

    private void OnTrigger(long period)
    {
      // Record that this period fired, unless a later one already did
      var fired = Volatile.Read(ref firedPeriod_);
      while (fired < period)
      {
        var previous = Interlocked.CompareExchange(ref firedPeriod_,
                                                   period,
                                                   fired);
        if (previous == fired)
        {
          break;
        }

        fired = previous;
      }

      TryComplete(period,
                  false);
    }

    // Complete the current wait if it is still the given one
    private void TryComplete(long wait,
                             bool nextCompleted)
    {
      if (Interlocked.CompareExchange(ref waiting_,
                                      NotWaiting,
                                      wait) == wait)
      {
        core_.SetResult(nextCompleted);
      }
    }

    /// <summary>
    ///   Signals that the trigger fired for a given period, ignored if the period is over.
    ///   The signal is forwarded to the thread pool, so that the enumeration never continues on the thread calling
    ///   <see cref="ChunkFlusher.Flush" />.
    /// </summary>
    private sealed class PeriodSignal
    {
      private static readonly WaitCallback Fire = static state =>
                                                  {
                                                    var signal = (PeriodSignal)state!;
                                                    signal.owner_.OnTrigger(signal.period_);
                                                  };

      public static readonly Action<object?> OnCancel = static state => ThreadPool.UnsafeQueueUserWorkItem(Fire,
                                                                                                           state);

      private readonly FlushTrigger owner_;
      private readonly long         period_;

      public PeriodSignal(FlushTrigger owner,
                          long         period)
      {
        owner_  = owner;
        period_ = period;
      }
    }
  }
}

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
  // Implementation of the Rechunk and ToChunksAsync functions
  // Chunker splits and merges the chunks, FlushTrigger decides when buffered data must be yielded early.
  // Errors are rethrown once all the data received so far has been yielded.
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

    // Used to cancel a fetch still in flight when the enumeration stops (only possible with a trigger)
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

        // Data is buffered and the source is not ready: wait for the source or the trigger.
        // A hooked fetch must be awaited through the trigger.
        if (trigger is not null && (trigger.IsHooked || (trigger.IsArmed && !next.IsCompleted)))
        {
          trigger.Hook(next);
          while (!trigger.NextCompleted && !trigger.IsDue)
          {
            trigger.Watch();
            await trigger.WaitAsync()
                         .ConfigureAwait(false);
          }

          // Checked first, so that available data wins
          if (!trigger.NextCompleted)
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

        // Deadline starts when the data is received, not when it is buffered
        var arrival = Environment.TickCount;

        chunker.Push(adapter.ToMemory(enumerator.Current));
        while (chunker.TryPop(out var chunk))
        {
          // The buffer is empty when a chunk is produced
          trigger?.Disarm();
          yield return adapter.FromMemory(chunk);
        }

        if (trigger is not null && !chunker.IsEmpty)
        {
          if (!trigger.IsArmed)
          {
            trigger.Arm(arrival);
          }

          // Synchronous sources can also be slow
          if (trigger.IsDue)
          {
            trigger.Disarm();
            yield return adapter.FromMemory(chunker.Flush());
          }
        }
      }

      // The last chunk must be yielded even if there is an error
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
            while (!trigger.NextCompleted)
            {
              await trigger.WaitAsync()
                           .ConfigureAwait(false);
            }
          }

          await next.ConfigureAwait(false);
        }
        catch
        {
          // Ignored: the enumeration is over
        }
      }

      await enumerator.DisposeAsync()
                      .ConfigureAwait(false);
    }

    error?.RethrowWithStacktrace();
  }

  // Returns Timeout.InfiniteTimeSpan if there is no timeout
  internal static TimeSpan ValidateMaxDelay(TimeSpan? maxDelay)
  {
    var delay = maxDelay ?? Timeout.InfiniteTimeSpan;
    if (delay != Timeout.InfiniteTimeSpan && (delay < TimeSpan.Zero || delay.TotalMilliseconds > int.MaxValue))
    {
      throw new ArgumentOutOfRangeException(nameof(maxDelay));
    }

    return delay;
  }

  // Converts source items to chunks, and chunks to yielded items.
  // Implemented by structs to be inlined. Each enumeration has its own copy.
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

  // Splits and merges input chunks into chunks between minSize and maxSize.
  // Only the data to merge is copied, into a buffer. Chunks built from it are arrays of the exact size.
  // Once TryPop returns false, the input chunk is not referenced anymore, so the source can reuse its memory.
  private sealed class Chunker<T>
  {
    private readonly int maxSize_;
    private readonly int minSize_;

    // Grows up to minSize. Only the first count_ elements are valid, and count_ < minSize_
    private T[]? buffer_;
    private int  count_;
    private int  nextCapacity_;

    // Remaining part of the current input chunk
    private ReadOnlyMemory<T> pending_;

    public Chunker(int minSize,
                   int maxSize)
    {
      minSize_ = minSize;
      maxSize_ = maxSize;
    }

    public bool IsEmpty
      => count_ == 0;

    // The previous input chunk must have been entirely consumed
    public void Push(ReadOnlyMemory<T> input)
      => pending_ = input;

    // Returns false once the input chunk has been entirely consumed. The buffer is empty when a chunk is produced.
    public bool TryPop(out ReadOnlyMemory<T> chunk)
    {
      chunk = default;
      var length = pending_.Length;
      if (length == 0)
      {
        return false;
      }

      // Not enough data for a chunk
      if (count_ + length < minSize_)
      {
        Append(pending_.Span);
        pending_ = default;
        return false;
      }

      int size;
      if (count_ > 0)
      {
        // If the remainder would be too small to be sliced, it would be copied anyway: absorb as much as possible
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
        size = SliceSize(length);
        chunk = pending_.Slice(0,
                               size);
      }

      pending_ = pending_.Slice(size);
      return true;
    }

    public ReadOnlyMemory<T> Flush()
    {
      var count = count_;
      count_ = 0;

      if (count == buffer_!.Length)
      {
        // Hand out the full buffer, the next one is allocated with the same size
        var full = buffer_;
        buffer_       = null;
        nextCapacity_ = count;
        return full;
      }

      return buffer_.AsSpan(0,
                            count)
                    .ToArray();
    }

    private ReadOnlyMemory<T> Merge(ReadOnlySpan<T> items)
    {
      if (count_ + items.Length == minSize_)
      {
        Append(items);
        return Flush();
      }

      // Larger than minSize: keep the buffer for later
      var array = new T[count_ + items.Length];
      buffer_.AsSpan(0,
                     count_)
             .CopyTo(array);
      items.CopyTo(array.AsSpan(count_));
      count_ = 0;
      return array;
    }

    // The total must not exceed minSize
    private void Append(ReadOnlySpan<T> items)
    {
      var required = count_ + items.Length;
      if (buffer_ is null || buffer_.Length < required)
      {
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

    // length >= minSize
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
        // Leave minSize elements so that the remainder can also be sliced
        return length - minSize_;
      }

      // Cannot be split in two valid chunks: minimize the remainder to copy
      return maxSize_;
    }
  }

  // Fires when the deadline of the buffered data expires, or when the flusher is triggered.
  // Armed while data is buffered: a flush requested while not armed is ignored.
  //
  // The completion of the fetch, the deadline and the flusher only signal the enumeration, which then checks the
  // state again: a late or spurious signal is harmless. The fetch is "hooked" with a continuation to signal its
  // completion. As a fetch accepts only one continuation, a hooked fetch must be awaited through the trigger.
  private sealed class FlushTrigger : IValueTaskSource, IDisposable
  {
    // States of the signal, which has a single waiter
    private const int Idle     = 0;
    private const int Waiting  = 1;
    private const int Signaled = 2;

    private static readonly WaitCallback SignalCallback = static state => ((FlushTrigger)state!).Signal();

    private readonly int           delayMs_; // Timeout.Infinite if there is no deadline
    private readonly ChunkFlusher? flusher_;
    private readonly Action        onNextCompleted_;

    private ManualResetValueTaskSourceCore<bool> core_;
    private int                                  deadline_; // Environment.TickCount, read by the timer
    private CancellationTokenRegistration        flushRegistration_;
    private CancellationToken                    flushToken_;
    private int                                  nextCompleted_;
    private int                                  start_;
    private bool                                 started_; // Whether the timer and the flusher are watched
    private int                                  state_;
    private Timer?                               timer_; // Reused across periods

    public FlushTrigger(TimeSpan      maxDelay,
                        ChunkFlusher? flusher)
    {
      delayMs_ = maxDelay == Timeout.InfiniteTimeSpan
                   ? Timeout.Infinite
                   : (int)Math.Ceiling(maxDelay.TotalMilliseconds);
      flusher_ = flusher;
      onNextCompleted_ = () =>
                         {
                           Volatile.Write(ref nextCompleted_,
                                          1);
                           Signal();
                         };
    }

    public bool IsArmed  { get; private set; }
    public bool IsHooked { get; private set; }

    public bool NextCompleted
      => Volatile.Read(ref nextCompleted_) != 0;

    public bool IsDue
      => IsArmed && (flushToken_.IsCancellationRequested || (delayMs_ != Timeout.Infinite && Remaining <= 0));

    private int Remaining
      => delayMs_ - unchecked(Environment.TickCount - start_);

    public void Dispose()
    {
      Disarm();
      timer_?.Dispose();
    }

    void IValueTaskSource.GetResult(short token)
    {
      core_.GetResult(token);
      Interlocked.Exchange(ref state_,
                           Idle);
    }

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

    // start: Environment.TickCount when the data was received
    public void Arm(int start)
    {
      IsArmed     = true;
      start_      = start;
      flushToken_ = flusher_?.Token ?? default;
    }

    public void Disarm()
    {
      IsArmed     = false;
      flushToken_ = default;
      if (!started_)
      {
        return;
      }

      started_ = false;
      flushRegistration_.Dispose();
      timer_?.Change(Timeout.Infinite,
                     Timeout.Infinite);
    }

    // Registers a continuation on next, unless already done
    public void Hook(ValueTask<bool> next)
    {
      if (IsHooked)
      {
        return;
      }

      IsHooked = true;
      Volatile.Write(ref nextCompleted_,
                     0);
      next.ConfigureAwait(false)
          .GetAwaiter()
          .UnsafeOnCompleted(onNextCompleted_);
    }

    // Must be called once the completed fetch has been consumed
    public void Unhook()
      => IsHooked = false;

    // Watches the deadline and the flusher while armed
    public void Watch()
    {
      if (!IsArmed || started_)
      {
        return;
      }

      started_ = true;
      if (flushToken_.CanBeCanceled)
      {
        // Forwarded to the thread pool, so that the enumeration never continues on the thread calling Flush
        flushRegistration_ = flushToken_.Register(static state => ThreadPool.UnsafeQueueUserWorkItem(SignalCallback,
                                                                                                     state),
                                                  this);
      }

      if (delayMs_ != Timeout.Infinite)
      {
        if (timer_ is null)
        {
          // The timer must not capture the execution context of the enumeration
          using (ExecutionContext.SuppressFlow())
          {
            timer_ = new Timer(static state => ((FlushTrigger)state!).OnTimer(),
                               this,
                               Timeout.Infinite,
                               Timeout.Infinite);
          }
        }

        Volatile.Write(ref deadline_,
                       unchecked(start_ + delayMs_));
        timer_.Change(Math.Max(Remaining,
                               0),
                      Timeout.Infinite);
      }
    }

    // Completes once signaled since the previous wait
    public ValueTask WaitAsync()
    {
      core_.Reset();
      if (Interlocked.CompareExchange(ref state_,
                                      Waiting,
                                      Idle) == Idle)
      {
        return new ValueTask(this,
                             core_.Version);
      }

      // Already signaled
      Interlocked.Exchange(ref state_,
                           Idle);
      return default;
    }

    private void Signal()
    {
      if (Interlocked.Exchange(ref state_,
                               Signaled) == Waiting)
      {
        core_.SetResult(true);
      }
    }

    // May be late: re-armed if the deadline is not reached yet
    private void OnTimer()
    {
      var remaining = unchecked(Volatile.Read(ref deadline_) - Environment.TickCount);
      if (remaining <= 0)
      {
        Signal();
        return;
      }

      try
      {
        timer_!.Change(remaining,
                       Timeout.Infinite);
      }
      catch (ObjectDisposedException)
      {
        // The enumeration is over
      }
    }
  }
}

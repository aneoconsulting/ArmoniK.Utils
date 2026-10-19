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
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using JetBrains.Annotations;

namespace ArmoniK.Utils;

/// <summary>
///   Limit the access to a function call to a single thread,
///   all the others waiting the result from the one actually doing the call.
/// </summary>
/// <typeparam name="T">Type of the return object</typeparam>
[PublicAPI]
public class ExecutionSingleizer<T> : IDisposable
{
  private readonly long   tickValidity_;
  private          Handle handle_ = new();

  /// <summary>
  ///   Allow initialization of <see cref="ExecutionSingleizer{T}" />
  /// </summary>
  /// <param name="timeValidity">
  ///   Outcome of the execution, result or exception, will be in cache during timeValidity.
  ///   A cancelled execution is not cached.
  /// </param>
  public ExecutionSingleizer(TimeSpan timeValidity = default)
    => tickValidity_ = (long)Math.Ceiling(Stopwatch.Frequency * timeValidity.TotalSeconds);

  /// <summary>
  ///   Cancel the execution in flight, if any.
  ///   Callers waiting on it receive either its result, if func completes anyway, or a cancellation.
  /// </summary>
  /// <remarks>
  ///   The current handle is not disposed, as callers may still be using it.
  ///   Its task and cancellation token source hold no resource that the GC cannot reclaim.
  /// </remarks>
  public void Dispose()
    => handle_.CancellationTokenSource.Cancel();

  /// <summary>
  ///   Call the asynchronous function func.
  ///   If another thread is already computing func, just wait for the result.
  ///   The actual call to func is cancelled only when all callers have cancelled it.
  /// </summary>
  /// <param name="func">Function to call</param>
  /// <param name="cancellationToken">Token to cancel the call</param>
  /// <returns>
  ///   Result of func
  /// </returns>
  public async Task<T> Call(Func<CancellationToken, Task<T>> func,
                            CancellationToken                cancellationToken = default)
  {
    // Read the handle_ reference to have a stable view of it
    var currentHandle = handle_;

    // If the task is complete (success or failed), or will be cancelled because all its callers have left,
    // we need to call func again if the data has expired.
    // The validity is set before the task completes, so it is always up to date once the task is complete.
    // Callers still waiting on a complete task do not prevent the refresh.
    if ((currentHandle.InnerTask.IsCompleted || currentHandle.CancellationTokenSource.IsCancellationRequested) && Stopwatch.GetTimestamp() > currentHandle.ValidUntil)
    {
      // Prepare new handle, with new cancellation token source and new task.
      // The task is not started yet: func must not be called before the handle is published.
      var newHandle = new Handle(func,
                                 tickValidity_);

      // Try to store new handle replacing the previous one.
      // Only one thread will succeed here.
      var previousHandle = Interlocked.CompareExchange(ref handle_,
                                                       newHandle,
                                                       currentHandle);

      // Check if thread successfully replaced the handle.
      if (ReferenceEquals(previousHandle,
                          currentHandle))
      {
        // Current thread has won, the others will see this new handle.
        // We can now start the task, other threads will just wait on the result.
        newHandle.Start();
        currentHandle = newHandle;

        // The previous handle is not disposed, as callers may still be waiting on it.
        // Disposing tasks is not needed anyway:
        // https://devblogs.microsoft.com/pfxteam/do-i-need-to-dispose-of-tasks/
      }
      else
      {
        // Record current thread as waiting for the task of the handle that won,
        // which is the one decremented once the result is available.
        currentHandle = previousHandle;
        Interlocked.Increment(ref currentHandle.Waiters);

        // The handle has been replaced by another thread, so we can just wait for the task
        // in this new handle to get the result.
        // The handle created by the current thread can be destroyed as it is not used by anything.
        // Before destroying the task, it must be complete (ran, faulted or cancelled).
        newHandle.CancellationTokenSource.Cancel();
        newHandle.Start();
        try
        {
          await newHandle.InnerTask.ConfigureAwait(false);
        }
        catch (TaskCanceledException)
        {
        }

        newHandle.Dispose();
      }
    }
    else
    {
      // if the task is not complete, we can just wait its result.
      // Record current thread as waiting for the task.
      Interlocked.Increment(ref currentHandle.Waiters);
    }

    var task = currentHandle.InnerTask;
    // Wait for task.
    try
    {
      // Wait for either the task to finish, or the cancellation token to be cancelled.
      return await Task.WhenAny(task,
                                cancellationToken.AsTask<T>())
                       .Unwrap()
                       .ConfigureAwait(false);
    }
    finally
    {
      // Remove the current thread from the list of waiters.
      var i = Interlocked.Decrement(ref currentHandle.Waiters);

      // If the current thread was the last, we can cancel the shared token.
      if (i == 0)
      {
        // If we enter here because the task has completed without errors,
        // cancelling is a no op, therefore, we do not need to check why we went here.
        currentHandle.CancellationTokenSource.Cancel();
      }
    }
  }

  /// <summary>
  ///   This handle stores a Task, a cancellationTokenSource, and a counter.
  ///   This needs to be a class to enable an atomic CAS.
  ///   It cannot be inlined into the parent class.
  /// </summary>
  private sealed class Handle : IDisposable
  {
    /// <summary>
    ///   Task calling func, created but not started, so that func is not called before the handle is published.
    ///   It is null for the initial cancelled handle.
    /// </summary>
    private readonly Task<Task<T>>? delayedTask_;

    /// <summary>
    ///   Specify the timestamp until the data is valid
    /// </summary>
    public long ValidUntil = long.MinValue;

    /// <summary>
    ///   Number of threads waiting for the result.
    /// </summary>
    public int Waiters;

    /// <summary>
    ///   Construct an handle that is cancelled.
    /// </summary>
    public Handle()
    {
      // Create a CancellationTokenSource that is already cancelled.
      CancellationTokenSource = new CancellationTokenSource();
      CancellationTokenSource.Cancel();

      // InnerTask is created cancelled.
      // As a cancelled task is completed, the result of this task will never be read,
      // and another task will always be created instead.
      InnerTask = Task.FromCanceled<T>(CancellationTokenSource.Token);
    }

    /// <summary>
    ///   Construct an handle whose task calls func once started.
    ///   The current thread is implicitly waiting for the task.
    /// </summary>
    /// <param name="func">Function to call</param>
    /// <param name="tickValidity">Validity of the outcome, in Stopwatch ticks</param>
    public Handle(Func<CancellationToken, Task<T>> func,
                  long                             tickValidity)
    {
      CancellationTokenSource = new CancellationTokenSource();
      Waiters                 = 1;

      // If the handle is never published, its token is cancelled before the task is started, and func is not called.
      delayedTask_ = new Task<Task<T>>(() => CancellationTokenSource.IsCancellationRequested
                                               ? Task.FromCanceled<T>(CancellationTokenSource.Token)
                                               : CallAndValidate(func,
                                                                 tickValidity));

      // Unwrap allows the handle to have a single level Task, instead of a Task<Task<...>>
      InnerTask = delayedTask_.Unwrap();
    }

    /// <summary>
    ///   Shared cancellation token for all the threads waiting on the task.
    /// </summary>
    public CancellationTokenSource CancellationTokenSource { get; }

    /// <summary>
    ///   Task that creates the result.
    /// </summary>
    public Task<T> InnerTask { get; }

    /// <inheritdoc />
    public void Dispose()
    {
      CancellationTokenSource.Dispose();
      InnerTask.Dispose();
    }

    /// <summary>
    ///   Start the task of the handle.
    ///   Must be called exactly once on a handle constructed with func.
    /// </summary>
    public void Start()
      => delayedTask_!.Start();

    /// <summary>
    ///   Call func, and set the validity of the handle once func has completed, but before the task of the handle completes.
    ///   The validity period therefore starts once, when the outcome is available, and is not extended by later callers.
    ///   A cancelled execution is not cached.
    /// </summary>
    /// <param name="func">Function to call</param>
    /// <param name="tickValidity">Validity of the outcome, in Stopwatch ticks</param>
    /// <returns>
    ///   Result of func
    /// </returns>
    private async Task<T> CallAndValidate(Func<CancellationToken, Task<T>> func,
                                          long                             tickValidity)
    {
      T result;
      try
      {
        result = await func(CancellationTokenSource.Token)
                   .ConfigureAwait(false);
      }
      catch (Exception e) when (e is not OperationCanceledException)
      {
        Validate(tickValidity);
        throw;
      }

      Validate(tickValidity);
      return result;
    }

    /// <summary>
    ///   Set the validity of the outcome, unless the execution has been cancelled.
    /// </summary>
    /// <param name="tickValidity">Validity of the outcome, in Stopwatch ticks</param>
    private void Validate(long tickValidity)
    {
      if (!CancellationTokenSource.IsCancellationRequested)
      {
        ValidUntil = Stopwatch.GetTimestamp() + tickValidity;
      }
    }
  }
}

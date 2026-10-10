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
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using JetBrains.Annotations;

namespace ArmoniK.Utils;

// Tasks spawned by ParallelSelect are cancelled and awaited when the enumeration ends.
// If the enumerator is never disposed, they are only cancelled from the finalizer, and may outlive the enumeration.
// They must therefore not use any object disposed at the end of the enumeration:
// - tokens are captured before any disposal, and their sources are cancelled before being disposed
// - semaphores are not disposed (SemaphoreSlim only needs disposal when its AvailableWaitHandle is used)
// - tasks report errors through a CancellationTokenSource that is never disposed
[PublicAPI]
internal static class ParallelSelectInternal
{
  /// <summary>
  ///   Iterates over the input enumerable and spawn multiple parallel tasks that call `func`.
  /// </summary>
  /// <param name="enumerable">Enumerable to iterate on</param>
  /// <param name="func">Function to spawn on the enumerable input, cancelled when the enumeration ends</param>
  /// <param name="parallelism">Maximum number of tasks running</param>
  /// <param name="bufferLimit">Maximum number of tasks started whose result has not been yielded yet</param>
  /// <param name="cancellationToken">Trigger cancellation of the enumeration</param>
  /// <typeparam name="TInput">Type of the inputs</typeparam>
  /// <typeparam name="TOutput">Type of the outputs</typeparam>
  /// <returns>Asynchronous results of func over the inputs</returns>
  internal static async IAsyncEnumerable<TOutput> ParallelSelectOrdered<TInput, TOutput>(IAsyncEnumerable<TInput>                       enumerable,
                                                                                         Func<TInput, CancellationToken, Task<TOutput>> func,
                                                                                         int                                            parallelism,
                                                                                         int                                            bufferLimit,
                                                                                         [EnumeratorCancellation] CancellationToken     cancellationToken)
  {
    // CancellationTokenSource used to cancel all tasks inflight upon errors
    var globalCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    // CancellationTokenSource cancelled by the tasks upon errors, never disposed as tasks may outlive the enumeration
    var errorCts = new CancellationTokenSource();
    var iterationCts = CancellationTokenSource.CreateLinkedTokenSource(globalCts.Token,
                                                                       errorCts.Token);
    var globalToken    = globalCts.Token;
    var iterationToken = iterationCts.Token;

    // Ensure all running tasks are actually aborted at the end, and unregister from cancellationToken.
    // If the enumerator is not disposed, the deferrer finalizer does it: otherwise, globalCts would
    // stay registered on cancellationToken, leaking memory if cancellationToken is long-lived.
    await using var cleanup = new Deferrer(() =>
                                           {
                                             try
                                             {
                                               globalCts.Cancel();
                                             }
                                             finally
                                             {
                                               iterationCts.Dispose();
                                               globalCts.Dispose();
                                             }
                                           });

    // Output
    var channel = Channel.CreateUnbounded<Task<TOutput>>();

    // Semaphores to limit the number of pending results, and the parallelism if it is lower
    var bufferSem = new SemaphoreSlim(bufferLimit);
    var parallelismSem = parallelism < bufferLimit
                           ? new SemaphoreSlim(parallelism)
                           : null;

    // Calls func on the thread pool, so that its synchronous part does not block the producer
    async Task<TOutput> Call(TInput x)
    {
      // Task.Yield is cheaper, but continues on the current context, that may not be the thread pool
      if (IsThreadPoolContext())
      {
        await Task.Yield();
      }
      else
      {
        await YieldToThreadPool();
      }

      // Do not start func once the enumeration has ended
      globalToken.ThrowIfCancellationRequested();

      try
      {
        // Not cancelled upon errors, as previous results must still be yielded
        return await func(x,
                          globalToken)
                 .ConfigureAwait(false);
      }
      catch
      {
        errorCts.Cancel();
        throw;
      }
      finally
      {
        parallelismSem?.Release();
      }
    }

    [SuppressMessage("ReSharper",
                     "PossibleMultipleEnumeration")]
    async Task Run()
    {
      try
      {
        await foreach (var x in enumerable.WithCancellation(iterationToken))
        {
          await bufferSem.WaitAsync(iterationToken)
                         .ConfigureAwait(false);
          if (parallelismSem is not null)
          {
            await parallelismSem.WaitAsync(iterationToken)
                                .ConfigureAwait(false);
          }

          var task = Call(x);

          channel.Writer.TryWrite(task);
        }
      }
      finally
      {
        channel.Writer.TryComplete();
      }
    }

    // Not cancellable, so that the producer always completes the channel
    var run = Task.Run(Run);

    try
    {
      await foreach (var task in channel.Reader.ToAsyncEnumerable(globalToken))
      {
        var res = await task.ConfigureAwait(false);

        bufferSem.Release();

        yield return res;
      }

      await run.ConfigureAwait(false);
    }
    finally
    {
      try
      {
        // Stop the producer and the running tasks
        globalCts.Cancel();
      }
      finally
      {
        // Wait for the producer, then for all the tasks it has started.
        // Their errors are ignored: only the first one is reported to the consumer.
        try
        {
          await run.ConfigureAwait(false);
        }
        catch
        {
          // ignored
        }

        while (channel.Reader.TryRead(out var task))
        {
          try
          {
            await task.ConfigureAwait(false);
          }
          catch
          {
            // ignored
          }
        }
      }
    }
  }

  /// <summary>
  ///   Iterates over the input enumerable and spawn multiple parallel tasks that call `func`.
  /// </summary>
  /// <param name="enumerable">Enumerable to iterate on</param>
  /// <param name="func">Function to spawn on the enumerable input, cancelled when the enumeration ends</param>
  /// <param name="parallelism">Maximum number of tasks running</param>
  /// <param name="bufferLimit">Maximum number of tasks started whose result has not been yielded yet</param>
  /// <param name="cancellationToken">Trigger cancellation of the enumeration</param>
  /// <typeparam name="TInput">Type of the inputs</typeparam>
  /// <typeparam name="TOutput">Type of the outputs</typeparam>
  /// <returns>Asynchronous results of func over the inputs</returns>
  internal static async IAsyncEnumerable<TOutput> ParallelSelectUnordered<TInput, TOutput>(IAsyncEnumerable<TInput>                       enumerable,
                                                                                           Func<TInput, CancellationToken, Task<TOutput>> func,
                                                                                           int                                            parallelism,
                                                                                           int                                            bufferLimit,
                                                                                           [EnumeratorCancellation] CancellationToken     cancellationToken)
  {
    // CancellationTokenSource used to cancel all tasks inflight upon errors
    var globalCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    // CancellationTokenSource cancelled by the tasks upon errors, never disposed as tasks may outlive the enumeration
    var errorCts = new CancellationTokenSource();
    var iterationCts = CancellationTokenSource.CreateLinkedTokenSource(globalCts.Token,
                                                                       errorCts.Token);
    var globalToken    = globalCts.Token;
    var iterationToken = iterationCts.Token;

    // Ensure all running tasks are actually aborted at the end, and unregister from cancellationToken.
    // If the enumerator is not disposed, the deferrer finalizer does it: otherwise, globalCts would
    // stay registered on cancellationToken, leaking memory if cancellationToken is long-lived.
    await using var cleanup = new Deferrer(() =>
                                           {
                                             try
                                             {
                                               globalCts.Cancel();
                                             }
                                             finally
                                             {
                                               iterationCts.Dispose();
                                               globalCts.Dispose();
                                             }
                                           });

    // Output, completed with the first error if any
    var channel = Channel.CreateUnbounded<TOutput>();

    // Forward the error to the consumer
    void Fail(Exception e)
    {
      if (channel.Writer.TryComplete(e))
      {
        // The error is surfaced to the consumer, but older channel implementations
        // also store it in Completion that is never observed
        _ = channel.Reader.Completion.ContinueWith(t => _ = t.Exception,
                                                   CancellationToken.None,
                                                   TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                                                   TaskScheduler.Default);
      }
    }

    // Semaphores to limit the number of pending results, and the parallelism if it is lower
    var bufferSem = new SemaphoreSlim(bufferLimit);
    var parallelismSem = parallelism < bufferLimit
                           ? new SemaphoreSlim(parallelism)
                           : null;

    // References held by the producer and the tasks, completes the channel and `done` when reaching zero
    var nbRef = 1;
    var done  = new TaskCompletionSource<ValueTuple>(TaskCreationOptions.RunContinuationsAsynchronously);

    void Release()
    {
      if (Interlocked.Decrement(ref nbRef) == 0)
      {
        channel.Writer.TryComplete();
        done.TrySetResult(new ValueTuple());
      }
    }

    // Calls func on the thread pool, so that its synchronous part does not block the producer
    async Task Call(TInput x)
    {
      // Task.Yield is cheaper, but continues on the current context, that may not be the thread pool
      if (IsThreadPoolContext())
      {
        await Task.Yield();
      }
      else
      {
        await YieldToThreadPool();
      }

      try
      {
        if (iterationToken.IsCancellationRequested)
        {
          return;
        }

        TOutput res;
        try
        {
          // Also cancelled upon errors, as the next results would be discarded
          res = await func(x,
                           iterationToken)
                  .ConfigureAwait(false);
        }
        catch (Exception e)
        {
          // Forward the error and stop the iteration
          Fail(e);
          errorCts.Cancel();
          return;
        }
        finally
        {
          parallelismSem?.Release();
        }

        // Fails silently if the channel has already been completed by an error
        channel.Writer.TryWrite(res);
      }
      finally
      {
        Release();
      }
    }

    [SuppressMessage("ReSharper",
                     "PossibleMultipleEnumeration")]
    async Task Run()
    {
      try
      {
        await foreach (var x in enumerable.WithCancellation(iterationToken))
        {
          await bufferSem.WaitAsync(iterationToken)
                         .ConfigureAwait(false);
          if (parallelismSem is not null)
          {
            await parallelismSem.WaitAsync(iterationToken)
                                .ConfigureAwait(false);
          }

          // Increment reference counter *before* starting the task
          // to avoid counter going to zero before being incremented
          Interlocked.Increment(ref nbRef);

          _ = Call(x);
        }
      }
      catch (Exception e)
      {
        // Forward the error, unless a task has already completed the channel with its own error
        Fail(e);
      }
      finally
      {
        Release();
      }
    }

    // Not cancellable, so that the reference is always released
    _ = Task.Run(Run);

    try
    {
      await foreach (var res in channel.Reader.ToAsyncEnumerable(globalToken))
      {
        bufferSem.Release();
        yield return res;
      }
    }
    finally
    {
      try
      {
        // Stop the producer and the running tasks
        globalCts.Cancel();
      }
      finally
      {
        // Wait for the producer and all the tasks it has started
        await done.Task.ConfigureAwait(false);
      }
    }
  }

  /// <summary>
  ///   Whether <see cref="Task.Yield" /> would continue on the thread pool.
  /// </summary>
  /// <returns>True if there is no SynchronizationContext and the current TaskScheduler is the default one</returns>
  private static bool IsThreadPoolContext()
    => SynchronizationContext.Current is null && TaskScheduler.Current == TaskScheduler.Default;

  /// <summary>
  ///   Continues the current method on the thread pool.
  /// </summary>
  /// <remarks>
  ///   Unlike <see cref="Task.Yield" />, it ignores the current SynchronizationContext and TaskScheduler:
  ///   the producer may run on a foreign context if the source completes inline from it.
  /// </remarks>
  /// <returns>An awaitable that always continues on the thread pool</returns>
  private static ThreadPoolAwaitable YieldToThreadPool()
    => default;

  private readonly struct ThreadPoolAwaitable : ICriticalNotifyCompletion
  {
    private static readonly WaitCallback RunContinuation = state => ((Action)state!)();

    public ThreadPoolAwaitable GetAwaiter()
      => this;

    public bool IsCompleted
      => false;

    public void GetResult()
    {
    }

    public void OnCompleted(Action continuation)
      => ThreadPool.QueueUserWorkItem(RunContinuation,
                                      continuation);

    public void UnsafeOnCompleted(Action continuation)
      => ThreadPool.UnsafeQueueUserWorkItem(RunContinuation,
                                            continuation);
  }
}

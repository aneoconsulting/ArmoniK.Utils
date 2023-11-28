// This file is part of the ArmoniK project
//
// Copyright (C) ANEO, 2022-2023.All rights reserved.
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

[PublicAPI]
internal static class ParallelSelectInternal
{
  /// <summary>
  ///   Iterates over the input enumerable and spawn multiple parallel tasks that call `func`.
  /// </summary>
  /// <param name="enumerable">Enumerable to iterate on</param>
  /// <param name="func">Function to spawn on the enumerable input</param>
  /// <param name="parallelism">Maximum number of tasks in flight</param>
  /// <param name="cancellationToken">Trigger cancellation of the enumeration</param>
  /// <typeparam name="TInput">Type of the inputs</typeparam>
  /// <typeparam name="TOutput">Type of the outputs</typeparam>
  /// <returns>Asynchronous results of func over the inputs</returns>
  internal static async IAsyncEnumerable<TOutput> ParallelSelectOrdered<TInput, TOutput>(IAsyncEnumerable<TInput>                   enumerable,
                                                                                         Func<TInput, Task<TOutput>>                func,
                                                                                         int                                        parallelism,
                                                                                         [EnumeratorCancellation] CancellationToken cancellationToken)
  {
    // CancellationTokenSource used to cancel all tasks inflight upon errors
    var globalCts    = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    var iterationCts = CancellationTokenSource.CreateLinkedTokenSource(globalCts.Token);

    // Output
    var channel = Channel.CreateUnbounded<Task<TOutput>>();

    // Semaphore to limit the parallelism
    var sem = new SemaphoreSlim(parallelism);

    [SuppressMessage("ReSharper",
                     "PossibleMultipleEnumeration")]
    [SuppressMessage("ReSharper",
                     "AccessToDisposedClosure")]
    async Task Run()
    {
      try
      {
        var runningTasks = new List<Task>();

        await foreach (var x in enumerable.WithCancellation(iterationCts.Token))
        {
          await sem.WaitAsync(iterationCts.Token)
                   .ConfigureAwait(false);
          var task = Task.Run(async () =>
                              {
                                TOutput res;
                                try
                                {
                                  res = await func(x)
                                          .ConfigureAwait(false);
                                }
                                catch
                                {
                                  iterationCts.Cancel();
                                  throw;
                                }

                                sem.Release();
                                return res;
                              },
                              globalCts.Token);

          runningTasks.Add(task);

          await channel.Writer.WriteAsync(task,
                                          globalCts.Token)
                       .ConfigureAwait(false);
        }

        await runningTasks.WhenAll()
                          .ConfigureAwait(false);
      }
      finally
      {
        channel.Writer.Complete();
      }
    }

    var run = Task.Run(Run,
                       globalCts.Token);

    try
    {
      await foreach (var res in channel.Reader.ToAsyncEnumerable(globalCts.Token))
      {
        yield return await res.ConfigureAwait(false);
      }

      await run.ConfigureAwait(false);
    }
    finally
    {
      // Ensure all running tasks are actually aborted
      globalCts.Cancel();
    }

    sem.Dispose();
    iterationCts.Dispose();
    globalCts.Dispose();
  }

  /// <summary>
  ///   Iterates over the input enumerable and spawn multiple parallel tasks that call `func`.
  /// </summary>
  /// <param name="enumerable">Enumerable to iterate on</param>
  /// <param name="func">Function to spawn on the enumerable input</param>
  /// <param name="parallelism">Maximum number of tasks in flight</param>
  /// <param name="cancellationToken">Trigger cancellation of the enumeration</param>
  /// <typeparam name="TInput">Type of the inputs</typeparam>
  /// <typeparam name="TOutput">Type of the outputs</typeparam>
  /// <returns>Asynchronous results of func over the inputs</returns>
  internal static async IAsyncEnumerable<TOutput> ParallelSelectUnordered<TInput, TOutput>(IAsyncEnumerable<TInput>                   enumerable,
                                                                                           Func<TInput, Task<TOutput>>                func,
                                                                                           int                                        parallelism,
                                                                                           [EnumeratorCancellation] CancellationToken cancellationToken)
  {
    // CancellationTokenSource used to cancel all tasks inflight upon errors
    var globalCts    = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    var iterationCts = CancellationTokenSource.CreateLinkedTokenSource(globalCts.Token);

    // Output
    var channel = Channel.CreateUnbounded<TOutput>();

    // Semaphore to limit the parallelism
    var sem = new SemaphoreSlim(parallelism);

    [SuppressMessage("ReSharper",
                     "PossibleMultipleEnumeration")]
    [SuppressMessage("ReSharper",
                     "AccessToDisposedClosure")]
    async Task Run()
    {
      try
      {
        var runningTasks = new List<Task>();

        await foreach (var x in enumerable.WithCancellation(iterationCts.Token))
        {
          await sem.WaitAsync(iterationCts.Token)
                   .ConfigureAwait(false);
          var task = Task.Run(async () =>
                              {
                                TOutput res;
                                try
                                {
                                  res = await func(x)
                                          .ConfigureAwait(false);
                                }
                                catch
                                {
                                  iterationCts.Cancel();
                                  throw;
                                }

                                sem.Release();
                                await channel.Writer.WriteAsync(res,
                                                                iterationCts.Token)
                                             .ConfigureAwait(false);
                              },
                              globalCts.Token);

          runningTasks.Add(task);
        }

        await runningTasks.WhenAll()
                          .ConfigureAwait(false);
      }
      finally
      {
        channel.Writer.Complete();
      }
    }

    var run = Task.Run(Run,
                       globalCts.Token);

    try
    {
      await foreach (var res in channel.Reader.ToAsyncEnumerable(globalCts.Token))
      {
        yield return res;
      }

      await run.ConfigureAwait(false);
    }
    finally
    {
      // Ensure all running tasks are actually aborted
      globalCts.Cancel();
    }

    sem.Dispose();
    iterationCts.Dispose();
    globalCts.Dispose();
  }
}

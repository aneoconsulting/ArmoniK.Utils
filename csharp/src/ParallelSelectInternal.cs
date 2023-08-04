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
using System.Linq;
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
    var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

    // Output
    var channel = Channel.CreateUnbounded<Task<TOutput>>();

    // Semaphore to limit the parallelism
    // Semaphore is created with one resource already acquired
    var sem = new SemaphoreSlim(parallelism);

    async Task Run()
    {
      try
      {
        var tasks = await enumerable.SelectAwait(async x =>
                                                 {
                                                   await sem.WaitAsync(cts.Token)
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
                                                                           cts.Cancel();
                                                                           throw;
                                                                         }

                                                                         sem.Release();
                                                                         return res;
                                                                       },
                                                                       cts.Token);

                                                   await channel.Writer.WriteAsync(task,
                                                                                   cts.Token)
                                                                .ConfigureAwait(false);

                                                   return task;
                                                 })
                                    .ToListAsync(cts.Token)
                                    .ConfigureAwait(false);

        await tasks.WhenAll()
                   .ConfigureAwait(false);
      }
      catch
      {
        cts.Cancel();
        throw;
      }
      finally
      {
        channel.Writer.Complete();
      }
    }

    var run = Task.Run(Run,
                       cts.Token);

    await foreach (var res in channel.Reader.ToAsyncEnumerable(CancellationToken.None))
    {
      yield return await res.ConfigureAwait(false);
    }

    await run.ConfigureAwait(false);

    sem.Dispose();
    cts.Dispose();
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
    var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

    // Output
    var channel = Channel.CreateUnbounded<TOutput>();

    async Task Run()
    {
      try
      {
        // Semaphore to limit the parallelism
        // Semaphore is created with one resource already acquired
        var sem = new SemaphoreSlim(parallelism);

        var tasks = await enumerable.SelectAwait(async x =>
                                                 {
                                                   await sem.WaitAsync(cts.Token)
                                                            .ConfigureAwait(false);
                                                   return Task.Run(async () =>
                                                                   {
                                                                     TOutput res;
                                                                     try
                                                                     {
                                                                       res = await func(x)
                                                                               .ConfigureAwait(false);
                                                                     }
                                                                     catch (Exception e)
                                                                     {
                                                                       cts.Cancel();
                                                                       throw;
                                                                     }

                                                                     sem.Release();
                                                                     await channel.Writer.WriteAsync(res,
                                                                                                     cts.Token)
                                                                                  .ConfigureAwait(false);
                                                                   },
                                                                   cts.Token);
                                                 })
                                    .ToListAsync(cts.Token)
                                    .ConfigureAwait(false);

        await tasks.WhenAll()
                   .ConfigureAwait(false);

        sem.Dispose();
      }
      finally
      {
        _ = channel.Writer.TryComplete();
      }
    }

    var run = Task.Run(Run,
                       cts.Token);

    await foreach (var res in channel.Reader.ToAsyncEnumerable(CancellationToken.None))
    {
      yield return res;
    }

    await run.ConfigureAwait(false);

    cts.Dispose();
  }
}

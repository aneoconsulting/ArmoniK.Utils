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
using System.Linq;
using System.Threading.Tasks;

using JetBrains.Annotations;

namespace ArmoniK.Utils;

/// <summary>
///   Extension class for <see cref="IEnumerable{T}" /> and <see cref="IAsyncEnumerable{T}" /> to call `ParallelSelect`
/// </summary>
public static class ParallelSelectExt
{
  /// <summary>
  ///   Iterates over the input enumerable and spawn multiple parallel tasks that call `func`.
  ///   The maximum number of tasks in flight at any given moment is given in the `parallelTaskOptions`.
  ///   All results are collected in-order.
  /// </summary>
  /// <param name="enumerable">Enumerable to iterate on</param>
  /// <param name="parallelTaskOptions">Options (eg: parallelismLimit, cancellationToken)</param>
  /// <param name="func">Function to spawn on the enumerable input</param>
  /// <typeparam name="TInput">Type of the inputs</typeparam>
  /// <typeparam name="TOutput">Type of the outputs</typeparam>
  /// <returns>Asynchronous results of func over the inputs</returns>
  [PublicAPI]
  public static IAsyncEnumerable<TOutput> ParallelSelect<TInput, TOutput>(this IEnumerable<TInput>    enumerable,
                                                                          ParallelTaskOptions         parallelTaskOptions,
                                                                          Func<TInput, Task<TOutput>> func)
    => enumerable.ToAsyncEnumerable()
                 .ParallelSelect(parallelTaskOptions,
                                 func);

  /// <summary>
  ///   Iterates over the input enumerable and spawn multiple parallel tasks that call `func`.
  ///   The maximum number of tasks in flight at any given moment is given in the `parallelTaskOptions`.
  ///   All results are collected in-order.
  /// </summary>
  /// <param name="enumerable">Enumerable to iterate on</param>
  /// <param name="parallelTaskOptions">Options (eg: parallelismLimit, cancellationToken)</param>
  /// <param name="func">Function to spawn on the enumerable input</param>
  /// <typeparam name="TInput">Type of the inputs</typeparam>
  /// <typeparam name="TOutput">Type of the outputs</typeparam>
  /// <returns>Asynchronous results of func over the inputs</returns>
  [PublicAPI]
  public static IAsyncEnumerable<TOutput> ParallelSelect<TInput, TOutput>(this IAsyncEnumerable<TInput> enumerable,
                                                                          ParallelTaskOptions           parallelTaskOptions,
                                                                          Func<TInput, Task<TOutput>>   func)
    => parallelTaskOptions.Unordered
         ? ParallelSelectInternal.ParallelSelectUnordered(enumerable,
                                                          func,
                                                          parallelTaskOptions.ParallelismLimit,
                                                          parallelTaskOptions.CancellationToken)
         : ParallelSelectInternal.ParallelSelectOrdered(enumerable,
                                                        func,
                                                        parallelTaskOptions.ParallelismLimit,
                                                        parallelTaskOptions.CancellationToken);

  /// <summary>
  ///   Iterates over the input enumerable and spawn multiple parallel tasks that call `func`.
  ///   At most "number of thread" tasks will be running at any given time.
  ///   All results are collected in-order.
  /// </summary>
  /// <param name="enumerable">Enumerable to iterate on</param>
  /// <param name="func">Function to spawn on the enumerable input</param>
  /// <typeparam name="TInput">Type of the inputs</typeparam>
  /// <typeparam name="TOutput">Type of the outputs</typeparam>
  /// <returns>Asynchronous results of func over the inputs</returns>
  [PublicAPI]
  public static IAsyncEnumerable<TOutput> ParallelSelect<TInput, TOutput>(this IEnumerable<TInput>    enumerable,
                                                                          Func<TInput, Task<TOutput>> func)
    => enumerable.ParallelSelect(default,
                                 func);

  /// <summary>
  ///   Iterates over the input enumerable and spawn multiple parallel tasks that call `func`.
  ///   At most "number of thread" tasks will be running at any given time.
  ///   All results are collected in-order.
  /// </summary>
  /// <param name="enumerable">Enumerable to iterate on</param>
  /// <param name="func">Function to spawn on the enumerable input</param>
  /// <typeparam name="TInput">Type of the inputs</typeparam>
  /// <typeparam name="TOutput">Type of the outputs</typeparam>
  /// <returns>Asynchronous results of func over the inputs</returns>
  [PublicAPI]
  public static IAsyncEnumerable<TOutput> ParallelSelect<TInput, TOutput>(this IAsyncEnumerable<TInput> enumerable,
                                                                          Func<TInput, Task<TOutput>>   func)
    => enumerable.ParallelSelect(default,
                                 func);

  /// <summary>
  ///   Iterates over the input enumerable and spawn multiple parallel tasks that call `func`.
  ///   The maximum number of tasks in flight at any given moment is given in the `parallelTaskOptions`.
  ///   All results are collected in-order.
  /// </summary>
  /// <param name="enumerable">Enumerable to iterate on</param>
  /// <param name="parallelTaskOptions">Options (eg: parallelismLimit, cancellationToken)</param>
  /// <param name="func">Function to spawn on the enumerable input</param>
  /// <typeparam name="TInput">Type of the inputs</typeparam>
  /// <returns>Asynchronous results of func over the inputs</returns>
  [PublicAPI]
  public static Task ParallelForEach<TInput>(this IEnumerable<TInput> enumerable,
                                             ParallelTaskOptions      parallelTaskOptions,
                                             Func<TInput, Task>       func)
    => enumerable.ToAsyncEnumerable()
                 .ParallelForEach(parallelTaskOptions,
                                  func);


  /// <summary>
  ///   Iterates over the input enumerable and spawn multiple parallel tasks that call `func`.
  ///   The maximum number of tasks in flight at any given moment is given in the `parallelTaskOptions`.
  ///   All results are collected in-order.
  /// </summary>
  /// <param name="enumerable">Enumerable to iterate on</param>
  /// <param name="parallelTaskOptions">Options (eg: parallelismLimit, cancellationToken)</param>
  /// <param name="func">Function to spawn on the enumerable input</param>
  /// <typeparam name="TInput">Type of the inputs</typeparam>
  /// <returns>Asynchronous results of func over the inputs</returns>
  [PublicAPI]
  public static async Task ParallelForEach<TInput>(this IAsyncEnumerable<TInput> enumerable,
                                                   ParallelTaskOptions           parallelTaskOptions,
                                                   Func<TInput, Task>            func)
  {
    await foreach (var _ in enumerable.ParallelSelect(parallelTaskOptions,
                                                      async x =>
                                                      {
                                                        await func(x)
                                                          .ConfigureAwait(false);
                                                        return new ValueTuple();
                                                      }))
    {
    }
  }

  /// <summary>
  ///   Iterates over the input enumerable and spawn multiple parallel tasks that call `func`.
  ///   At most "number of thread" tasks will be running at any given time.
  ///   All results are collected in-order.
  /// </summary>
  /// <param name="enumerable">Enumerable to iterate on</param>
  /// <param name="func">Function to spawn on the enumerable input</param>
  /// <typeparam name="TInput">Type of the inputs</typeparam>
  /// <returns>Asynchronous results of func over the inputs</returns>
  [PublicAPI]
  public static Task ParallelForEach<TInput>(this IEnumerable<TInput> enumerable,
                                             Func<TInput, Task>       func)
    => enumerable.ParallelForEach(default,
                                  func);


  /// <summary>
  ///   Iterates over the input enumerable and spawn multiple parallel tasks that call `func`.
  ///   At most "number of thread" tasks will be running at any given time.
  ///   All results are collected in-order.
  /// </summary>
  /// <param name="enumerable">Enumerable to iterate on</param>
  /// <param name="func">Function to spawn on the enumerable input</param>
  /// <typeparam name="TInput">Type of the inputs</typeparam>
  /// <returns>Asynchronous results of func over the inputs</returns>
  [PublicAPI]
  public static Task ParallelForEach<TInput>(this IAsyncEnumerable<TInput> enumerable,
                                             Func<TInput, Task>            func)
    => enumerable.ParallelForEach(default,
                                  func);
}

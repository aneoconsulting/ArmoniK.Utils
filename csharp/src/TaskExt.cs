// This file is part of the ArmoniK project
//
// Copyright (C) ANEO, 2022-2024.All rights reserved.
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
using System.Threading;
using System.Threading.Tasks;

using JetBrains.Annotations;

namespace ArmoniK.Utils;

/// <summary>
///   Extension class for Tasks
/// </summary>
public static class TaskExt
{
  /// <summary>
  ///   Creates a continuation that executes asynchronously when the target <see cref="Task" /> completes successfully.
  /// </summary>
  /// <param name="task">The target task.</param>
  /// <param name="continuation">A function to run when the <paramref name="task" /> completes.</param>
  /// <returns>The continuation task.</returns>
  [PublicAPI]
  public static async Task AndThen(this Task task,
                                   Action    continuation)
  {
    await task.ConfigureAwait(false);
    continuation();
  }

  /// <summary>
  ///   Creates a continuation that executes asynchronously when the target <see cref="Task" /> completes successfully and
  ///   returns a value.
  /// </summary>
  /// <param name="task">The target task.</param>
  /// <param name="continuation">A function to run when the <paramref name="task" /> completes.</param>
  /// <typeparam name="TOut">The type of the result produced by the continuation.</typeparam>
  /// <returns>The continuation task.</returns>
  [PublicAPI]
  public static async Task<TOut> AndThen<TOut>(this Task  task,
                                               Func<TOut> continuation)
  {
    await task.ConfigureAwait(false);
    return continuation();
  }

  /// <summary>
  ///   Creates a continuation that executes asynchronously when the target <see cref="Task" /> completes successfully.
  /// </summary>
  /// <param name="task">The target task.</param>
  /// <param name="continuation">A function to run when the <paramref name="task" /> completes.</param>
  /// <returns>The continuation task.</returns>
  [PublicAPI]
  public static async Task AndThen(this Task  task,
                                   Func<Task> continuation)
  {
    await task.ConfigureAwait(false);
    await continuation()
      .ConfigureAwait(false);
  }

  /// <summary>
  ///   Creates a continuation that executes asynchronously when the target <see cref="Task" /> completes successfully and
  ///   returns a value.
  /// </summary>
  /// <param name="task">The target task.</param>
  /// <param name="continuation">A function to run when the <paramref name="task" /> completes.</param>
  /// <typeparam name="TOut">The type of the result produced by the continuation.</typeparam>
  /// <returns>The continuation task.</returns>
  [PublicAPI]
  public static async Task<TOut> AndThen<TOut>(this Task        task,
                                               Func<Task<TOut>> continuation)
  {
    await task.ConfigureAwait(false);
    return await continuation()
             .ConfigureAwait(false);
  }

  /// <summary>
  ///   Creates a continuation that executes asynchronously when the target <see cref="Task{TIn}" /> completes successfully.
  /// </summary>
  /// <param name="task">The target task.</param>
  /// <param name="continuation">
  ///   A function to run when the <paramref name="task" /> completes, whose input is the result of
  ///   the <paramref name="task" />.
  /// </param>
  /// <typeparam name="TIn">The type of the result of the target task.</typeparam>
  /// <returns>The continuation task.</returns>
  [PublicAPI]
  public static async Task AndThen<TIn>(this Task<TIn> task,
                                        Action<TIn>    continuation)
    => continuation(await task.ConfigureAwait(false));

  /// <summary>
  ///   Creates a continuation that executes asynchronously when the target <see cref="Task{TIn}" /> completes successfully
  ///   and returns a value.
  /// </summary>
  /// <param name="task">The target task.</param>
  /// <param name="continuation">
  ///   A function to run when the <paramref name="task" /> completes, whose input is the result of
  ///   the <paramref name="task" />.
  /// </param>
  /// <typeparam name="TIn">The type of the result of the target task.</typeparam>
  /// <typeparam name="TOut">The type of the result produced by the continuation.</typeparam>
  /// <returns>The continuation task.</returns>
  [PublicAPI]
  public static async Task<TOut> AndThen<TIn, TOut>(this Task<TIn>  task,
                                                    Func<TIn, TOut> continuation)
    => continuation(await task.ConfigureAwait(false));


  /// <summary>
  ///   Creates a continuation that executes asynchronously when the target <see cref="Task{TIn}" /> completes successfully.
  /// </summary>
  /// <param name="task">The target task.</param>
  /// <param name="continuation">
  ///   A function to run when the <paramref name="task" /> completes, whose input is the result of
  ///   the <paramref name="task" />.
  /// </param>
  /// <typeparam name="TIn">The type of the result of the target task.</typeparam>
  /// <returns>The continuation task.</returns>
  [PublicAPI]
  public static async Task AndThen<TIn>(this Task<TIn>  task,
                                        Func<TIn, Task> continuation)
    => await continuation(await task.ConfigureAwait(false))
         .ConfigureAwait(false);


  /// <summary>
  ///   Creates a continuation that executes asynchronously when the target <see cref="Task{TIn}" /> completes successfully
  ///   and returns a value.
  /// </summary>
  /// <param name="task">The target task.</param>
  /// <param name="continuation">
  ///   A function to run when the <paramref name="task" /> completes, whose input is the result of
  ///   the <paramref name="task" />.
  /// </param>
  /// <typeparam name="TIn">The type of the result of the target task.</typeparam>
  /// <typeparam name="TOut">The type of the result produced by the continuation.</typeparam>
  /// <returns>The continuation task.</returns>
  [PublicAPI]
  public static async Task<TOut> AndThen<TIn, TOut>(this Task<TIn>        task,
                                                    Func<TIn, Task<TOut>> continuation)
    => await continuation(await task.ConfigureAwait(false))
         .ConfigureAwait(false);


  /// <summary>
  ///   Creates a task that will complete when all of the supplied tasks have completed.
  /// </summary>
  /// <param name="tasks">The tasks to wait on for completion.</param>
  /// <returns>A task that represents the completion of all of the supplied tasks.</returns>
  /// <remarks>
  ///   <para>
  ///     If any of the supplied tasks completes in a faulted state, the returned task will also complete in a Faulted state,
  ///     where its exceptions will contain the aggregation of the set of unwrapped exceptions from each of the supplied
  ///     tasks.
  ///   </para>
  ///   <para>
  ///     If none of the supplied tasks faulted but at least one of them was canceled, the returned task will end in the
  ///     Canceled state.
  ///   </para>
  ///   <para>
  ///     If none of the tasks faulted and none of the tasks were canceled, the resulting task will end in the
  ///     RanToCompletion state.
  ///     The Result of the returned task will be set to an array containing all of the results of the
  ///     supplied tasks in the same order as they were provided (e.g. if the input tasks array contained t1, t2, t3, the
  ///     output
  ///     task's Result will return an TResult[] where arr[0] == t1.Result, arr[1] == t2.Result, and arr[2] == t3.Result).
  ///   </para>
  ///   <para>
  ///     If the supplied array/enumerable contains no tasks, the returned task will immediately transition to a
  ///     RanToCompletion
  ///     state before it's returned to the caller.  The returned TResult[] will be an array of 0 elements.
  ///   </para>
  /// </remarks>
  /// <exception cref="T:System.ArgumentNullException">
  ///   The <paramref name="tasks" /> argument was null.
  /// </exception>
  /// <exception cref="T:System.ArgumentException">
  ///   The <paramref name="tasks" /> collection contained a null task.
  /// </exception>
  [PublicAPI]
  public static Task<T[]> WhenAll<T>(this IEnumerable<Task<T>> tasks)
    => Task.WhenAll(tasks);

  /// <summary>
  ///   Creates a task that will complete when all of the supplied tasks have completed.
  /// </summary>
  /// <param name="tasks">The tasks to wait on for completion.</param>
  /// <returns>A task that represents the completion of all of the supplied tasks.</returns>
  /// <remarks>
  ///   <para>
  ///     If any of the supplied tasks completes in a faulted state, the returned task will also complete in a Faulted state,
  ///     where its exceptions will contain the aggregation of the set of unwrapped exceptions from each of the supplied
  ///     tasks.
  ///   </para>
  ///   <para>
  ///     If none of the supplied tasks faulted but at least one of them was canceled, the returned task will end in the
  ///     Canceled state.
  ///   </para>
  ///   <para>
  ///     If none of the tasks faulted and none of the tasks were canceled, the resulting task will end in the
  ///     RanToCompletion state.
  ///   </para>
  ///   <para>
  ///     If the supplied array/enumerable contains no tasks, the returned task will immediately transition to a
  ///     RanToCompletion
  ///     state before it's returned to the caller.
  ///   </para>
  /// </remarks>
  /// <exception cref="T:System.ArgumentNullException">
  ///   The <paramref name="tasks" /> argument was null.
  /// </exception>
  /// <exception cref="T:System.ArgumentException">
  ///   The <paramref name="tasks" /> collection contained a null task.
  /// </exception>
  [PublicAPI]
  public static Task WhenAll(this IEnumerable<Task> tasks)
    => Task.WhenAll(tasks);

  /// <summary>
  ///   Asynchronously generates a List of <typeparamref name="T" /> from an Enumerable of <typeparamref name="T" />
  /// </summary>
  /// <param name="enumerableTask">Task that generates an enumerable of <typeparamref name="T" /></param>
  /// <typeparam name="T">Element type of the enumerable</typeparam>
  /// <returns>A task that represents when the conversion has been performed</returns>
  [PublicAPI]
  public static async Task<List<T>> ToListAsync<T>(this Task<IEnumerable<T>> enumerableTask)
    => (await enumerableTask.ConfigureAwait(false)).ToList();

  /// <summary>
  ///   Convert a Cancellation Token into a Task.
  ///   The task will wait for the cancellation token to be cancelled and throw an exception.
  /// </summary>
  /// <param name="cancellationToken">Cancellation Token to convert</param>
  /// <typeparam name="T">Type of the (unused) result of the task</typeparam>
  /// <returns>Task that will be completed upon cancellation</returns>
  [PublicAPI]
  public static Task<T> AsTask<T>(this CancellationToken cancellationToken)
  {
    var tcs = new TaskCompletionSource<T>();
    cancellationToken.Register(() => tcs.SetCanceled());
    return tcs.Task;
  }

  /// <summary>
  ///   Convert a Cancellation Token into a Task.
  ///   The task will wait for the cancellation token to be cancelled and throw an exception.
  /// </summary>
  /// <param name="cancellationToken">Cancellation Token to convert</param>
  /// <returns>Task that will be completed upon cancellation</returns>
  [PublicAPI]
  public static Task AsTask(this CancellationToken cancellationToken)
    => Task.Delay(Timeout.Infinite,
                  cancellationToken);

  /// <summary>
  ///   If the task is in error (either cancelled or faulted),
  ///   the appropriate will be thrown.
  ///   Otherwise, nothing happens.
  /// </summary>
  /// <param name="task">Task to check status</param>
  /// <param name="cancellationTokenSource">Token source to signal if there is an error</param>
  [PublicAPI]
  public static void ThrowIfError(this Task                task,
                                  CancellationTokenSource? cancellationTokenSource = null)
  {
    switch (task.Status)
    {
      case TaskStatus.Canceled:
      case TaskStatus.Faulted:
        // Signal the token source, if any
        cancellationTokenSource?.Cancel();

        // Task is already completed, .WaitSync() will not block
        task.WaitSync();

        break;
      case TaskStatus.RanToCompletion:
        return;
      case TaskStatus.Created:
      case TaskStatus.Running:
      case TaskStatus.WaitingForActivation:
      case TaskStatus.WaitingForChildrenToComplete:
      case TaskStatus.WaitingToRun:
      default:
        return;
    }
  }

  /// <summary>
  ///   Synchronously wait a <see cref="Task" />.
  /// </summary>
  /// <param name="task">Task to be awaited</param>
  [PublicAPI]
  public static void WaitSync(this Task task)
    // Ensure exception is propagated without wrapping it into an AggregateException like .Result or .Wait() would.
    => task.GetAwaiter()
           .GetResult();

  /// <summary>
  ///   Synchronously wait a <see cref="Task{T}" />.
  /// </summary>
  /// <param name="task">Task to be awaited</param>
  /// <typeparam name="TResult">Type of the task result</typeparam>
  /// <returns>Result of the task</returns>
  [PublicAPI]
  public static TResult WaitSync<TResult>(this Task<TResult> task)
    // Ensure exception is propagated without wrapping it into an AggregateException like .Result or .Wait() would.
    => task.GetAwaiter()
           .GetResult();

  /// <summary>
  ///   Check if the <paramref name="task" /> is completed.
  ///   If the task is canceled or faulted, it will throw the according exception.
  /// </summary>
  /// <param name="task"><see cref="Task" /> to check.</param>
  /// <returns>Whether the task has completed.</returns>
  [PublicAPI]
  public static bool TryGetSync(this Task task)
  {
    if (task.IsCompleted)
    {
      task.WaitSync();
      return true;
    }

    return false;
  }

  /// <summary>
  ///   Check if the <paramref name="task" /> is completed and get its result.
  ///   If the task is canceled or faulted, it will throw the according exception.
  /// </summary>
  /// <param name="task"><see cref="Task{TOut}" /> to check.</param>
  /// <param name="result">The result of the <paramref name="task" />.</param>
  /// <typeparam name="TOut">Type of the result of the task.</typeparam>
  /// <returns>Whether the task has completed.</returns>
  [PublicAPI]
  [ContractAnnotation("=> false, result: null; => true, result:notnull")]
  public static bool TryGetSync<TOut>(this Task<TOut> task,
                                      out  TOut       result)
  {
    if (task.IsCompleted)
    {
      // Ensure exception is propagated without wrapping it into an AggregateException like .Result or .Wait() would.
      result = task.WaitSync();
      return true;
    }

#pragma warning disable CS8601 // Possible null reference assignment.
    result = default;
#pragma warning restore CS8601 // Possible null reference assignment.

    return false;
  }
}

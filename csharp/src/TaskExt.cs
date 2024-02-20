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
  ///   Synchronously wait a <see cref="ValueTask" />.
  /// </summary>
  /// <param name="task">Task to be awaited</param>
  [PublicAPI]
  public static void WaitSync(this ValueTask task)
  {
    if (task.IsCompleted)
    {
      // Ensure exception is propagated without wrapping it into an AggregateException like .Result or .Wait() would.
      task.GetAwaiter()
          .GetResult();
    }
    else
    {
      // Not already completed `ValueTask` cannot be safely synchronously waited in a direct way.
      // Converting to actual `Task` enable to wait for it safely.
      task.AsTask()
          .GetAwaiter()
          .GetResult();
    }
  }

  /// <summary>
  ///   Synchronously wait a <see cref="ValueTask{T}" />.
  /// </summary>
  /// <param name="task">Task to be awaited</param>
  /// <typeparam name="TResult">Type of the task result</typeparam>
  /// <returns>Result of the task</returns>
  [PublicAPI]
  public static TResult WaitSync<TResult>(this ValueTask<TResult> task)
    => task.IsCompleted
         // Ensure exception is propagated without wrapping it into an AggregateException like .Result or .Wait() would.
         ? task.GetAwaiter()
               .GetResult()
         // Not already completed `ValueTask` cannot be safely synchronously waited in a direct way.
         // Converting to actual `Task` enable to wait for it safely.
         : task.AsTask()
               .GetAwaiter()
               .GetResult();
}

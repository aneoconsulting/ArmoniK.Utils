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
using System.Threading;
using System.Threading.Tasks;

using JetBrains.Annotations;

namespace ArmoniK.Utils;

/// <summary>
///   Extension class for ValueTasks
/// </summary>
public static class ValueTaskExt
{
  /// <summary>
  ///   Gets a task that has already completed successfully.
  /// </summary>
  /// <returns>The successfully completed task.</returns>
  [PublicAPI]
  public static ValueTask CompletedTask
    => new();

  /// <summary>
  ///   Creates a <see cref="ValueTask" /> that has completed with the specified exception.
  /// </summary>
  /// <param name="exception">The exception with which to complete the task.</param>
  /// <returns>The faulted task.</returns>
  [PublicAPI]
  public static ValueTask FromException(Exception exception)
    => new(Task.FromException(exception));

  /// <summary>
  ///   Creates a <see cref="ValueTask{TResult}" /> that has completed with the specified exception.
  /// </summary>
  /// <param name="exception">The exception with which to complete the task.</param>
  /// <typeparam name="TResult">The type of the result returned by the task.</typeparam>
  /// <returns>The faulted task.</returns>
  [PublicAPI]
  public static ValueTask<TResult> FromException<TResult>(Exception exception)
    => new(Task.FromException<TResult>(exception));

  /// <summary>
  ///   Creates a <see cref="ValueTask" /> that's completed due to cancellation with a specified cancellation token.
  /// </summary>
  /// <param name="cancellationToken">The cancellation token with which to complete the task.</param>
  /// <returns>The canceled task.</returns>
  /// <exception cref="ArgumentOutOfRangeException">
  ///   Cancellation has not been requested for
  ///   <paramref name="cancellationToken" />; its <see cref="CancellationToken.IsCancellationRequested" /> property is
  ///   false.
  /// </exception>
  [PublicAPI]
  public static ValueTask FromCanceled(CancellationToken cancellationToken)
    => new(Task.FromCanceled(cancellationToken));

  /// <summary>
  ///   Creates a <see cref="ValueTask{TResult}" /> that's completed due to cancellation with a specified cancellation token.
  /// </summary>
  /// <param name="cancellationToken">The cancellation token with which to complete the task.</param>
  /// <typeparam name="TResult">The type of the result returned by the task.</typeparam>
  /// <returns>The canceled task.</returns>
  /// <exception cref="ArgumentOutOfRangeException">
  ///   Cancellation has not been requested for
  ///   <paramref name="cancellationToken" />; its <see cref="CancellationToken.IsCancellationRequested" /> property is
  ///   false.
  /// </exception>
  [PublicAPI]
  public static ValueTask<TResult> FromCanceled<TResult>(CancellationToken cancellationToken)
    => new(Task.FromCanceled<TResult>(cancellationToken));

  /// <summary>
  ///   Creates a <see cref="ValueTask{TResult}" /> that's completed successfully with the specified result.
  /// </summary>
  /// <param name="result">The result to store into the completed task.</param>
  /// <typeparam name="TResult">The type of the result returned by the task.</typeparam>
  /// <returns>The successfully completed task.</returns>
  [PublicAPI]
  public static ValueTask<TResult> FromResult<TResult>(TResult result)
    => new(result);

  /// <summary>
  ///   Creates a continuation that executes synchronously when the target <see cref="ValueTask" /> completes successfully.
  /// </summary>
  /// <remarks>
  ///   <para>
  ///     The target <paramref name="task" /> must not be awaited after this call.
  ///     Only the continuation task should be awaited.
  ///   </para>
  ///   <para>
  ///     If the <paramref name="task" /> is synchronously completed and fails or the continuation throws,
  ///     the call does not throw and returns a failed <see cref="ValueTask" />.
  ///   </para>
  /// </remarks>
  /// <param name="task">The target task.</param>
  /// <param name="continuation">A function to run when the <paramref name="task" /> completes.</param>
  /// <returns>The continuation task.</returns>
  [PublicAPI]
  public static ValueTask AndThen(this ValueTask task,
                                  Action         continuation)
  {
    try
    {
      if (task.TryGetResult())
      {
        continuation();
        return new ValueTask();
      }

      return Core(task,
                  continuation);
    }
    catch (OperationCanceledException ex)
    {
      return FromCanceled(ex.CancellationToken);
    }
    catch (Exception ex)
    {
      return FromException(ex);
    }

    static async ValueTask Core(ValueTask task,
                                Action    continuation)
    {
      await task.ConfigureAwait(false);
      continuation();
    }
  }

  /// <summary>
  ///   Creates a continuation that executes synchronously when the target <see cref="ValueTask" /> completes successfully
  ///   and returns a value.
  /// </summary>
  /// <remarks>
  ///   <para>
  ///     The target <paramref name="task" /> must not be awaited after this call.
  ///     Only the continuation task should be awaited.
  ///   </para>
  ///   <para>
  ///     If the <paramref name="task" /> is synchronously completed and fails or the continuation throws,
  ///     the call does not throw and returns a failed <see cref="ValueTask{TOut}" />.
  ///   </para>
  /// </remarks>
  /// <param name="task">The target task.</param>
  /// <param name="continuation">A function to run when the <paramref name="task" /> completes.</param>
  /// <typeparam name="TOut">The type of the result produced by the continuation.</typeparam>
  /// <returns>The continuation task.</returns>
  [PublicAPI]
  public static ValueTask<TOut> AndThen<TOut>(this ValueTask task,
                                              Func<TOut>     continuation)
  {
    try
    {
      return task.TryGetResult()
               ? new ValueTask<TOut>(continuation())
               : Core(task,
                      continuation);
    }
    catch (OperationCanceledException ex)
    {
      return FromCanceled<TOut>(ex.CancellationToken);
    }
    catch (Exception ex)
    {
      return FromException<TOut>(ex);
    }

    static async ValueTask<TOut> Core(ValueTask  task,
                                      Func<TOut> continuation)
    {
      await task.ConfigureAwait(false);
      return continuation();
    }
  }

  /// <summary>
  ///   Creates a continuation that executes asynchronously when the target <see cref="ValueTask" /> completes successfully.
  /// </summary>
  /// <remarks>
  ///   <para>
  ///     The target <paramref name="task" /> must not be awaited after this call.
  ///     Only the continuation task should be awaited.
  ///   </para>
  ///   <para>
  ///     If the <paramref name="task" /> is synchronously completed and fails or the continuation throws,
  ///     the call does not throw and returns a failed <see cref="ValueTask" />
  ///   </para>
  /// </remarks>
  /// <param name="task">The target task.</param>
  /// <param name="continuation">A function to run when the <paramref name="task" /> completes.</param>
  /// <returns>The continuation task.</returns>
  [PublicAPI]
  public static ValueTask AndThen(this ValueTask  task,
                                  Func<ValueTask> continuation)
  {
    try
    {
      return task.TryGetResult()
               ? continuation()
               : Core(task,
                      continuation);
    }
    catch (OperationCanceledException ex)
    {
      return FromCanceled(ex.CancellationToken);
    }
    catch (Exception ex)
    {
      return FromException(ex);
    }

    static async ValueTask Core(ValueTask       task,
                                Func<ValueTask> continuation)
    {
      await task.ConfigureAwait(false);
      await continuation()
        .ConfigureAwait(false);
    }
  }

  /// <summary>
  ///   Creates a continuation that executes asynchronously when the target <see cref="ValueTask" /> completes successfully
  ///   and returns a value.
  /// </summary>
  /// <remarks>
  ///   <para>
  ///     The target <paramref name="task" /> must not be awaited after this call.
  ///     Only the continuation task should be awaited.
  ///   </para>
  ///   <para>
  ///     If the <paramref name="task" /> is synchronously completed and fails or the continuation throws,
  ///     the call does not throw and returns a failed <see cref="ValueTask{TOut}" />.
  ///   </para>
  /// </remarks>
  /// <param name="task">The target task.</param>
  /// <param name="continuation">A function to run when the <paramref name="task" /> completes.</param>
  /// <typeparam name="TOut">The type of the result produced by the continuation.</typeparam>
  /// <returns>The continuation task.</returns>
  [PublicAPI]
  public static ValueTask<TOut> AndThen<TOut>(this ValueTask        task,
                                              Func<ValueTask<TOut>> continuation)
  {
    try
    {
      return task.TryGetResult()
               ? continuation()
               : Core(task,
                      continuation);
    }
    catch (OperationCanceledException ex)
    {
      return FromCanceled<TOut>(ex.CancellationToken);
    }
    catch (Exception ex)
    {
      return FromException<TOut>(ex);
    }

    static async ValueTask<TOut> Core(ValueTask             task,
                                      Func<ValueTask<TOut>> continuation)
    {
      await task.ConfigureAwait(false);
      return await continuation()
               .ConfigureAwait(false);
    }
  }

  /// <summary>
  ///   Creates a continuation that executes synchronously when the target <see cref="ValueTask{TIn}" /> completes
  ///   successfully.
  /// </summary>
  /// <remarks>
  ///   <para>
  ///     The target <paramref name="task" /> must not be awaited after this call.
  ///     Only the continuation task should be awaited.
  ///   </para>
  ///   <para>
  ///     If the <paramref name="task" /> is synchronously completed and fails or the continuation throws,
  ///     the call does not throw and returns a failed <see cref="ValueTask" />.
  ///   </para>
  /// </remarks>
  /// <param name="task">The target task.</param>
  /// <param name="continuation">A function to run when the <paramref name="task" /> completes.</param>
  /// <typeparam name="TIn">The type of the result of the target task.</typeparam>
  /// <returns>The continuation task.</returns>
  [PublicAPI]
  public static ValueTask AndThen<TIn>(this ValueTask<TIn> task,
                                       Action<TIn>         continuation)
  {
    try
    {
      if (task.TryGetResult(out var result))
      {
        continuation(result);
        return new ValueTask();
      }

      return Core(task,
                  continuation);
    }
    catch (OperationCanceledException ex)
    {
      return FromCanceled(ex.CancellationToken);
    }
    catch (Exception ex)
    {
      return FromException(ex);
    }

    static async ValueTask Core(ValueTask<TIn> task,
                                Action<TIn>    continuation)
      => continuation(await task.ConfigureAwait(false));
  }

  /// <summary>
  ///   Creates a continuation that executes synchronously when the target <see cref="ValueTask{TIn}" /> completes
  ///   successfully and returns a value.
  /// </summary>
  /// <remarks>
  ///   <para>
  ///     The target <paramref name="task" /> must not be awaited after this call.
  ///     Only the continuation task should be awaited.
  ///   </para>
  ///   <para>
  ///     If the <paramref name="task" /> is synchronously completed and fails or the continuation throws,
  ///     the call does not throw and returns a failed <see cref="ValueTask{TOut}" />.
  ///   </para>
  /// </remarks>
  /// <param name="task">The target task.</param>
  /// <param name="continuation">A function to run when the <paramref name="task" /> completes.</param>
  /// <typeparam name="TIn">The type of the result of the target task.</typeparam>
  /// <typeparam name="TOut">The type of the result produced by the continuation.</typeparam>
  /// <returns>The continuation task.</returns>
  [PublicAPI]
  public static ValueTask<TOut> AndThen<TIn, TOut>(this ValueTask<TIn> task,
                                                   Func<TIn, TOut>     continuation)
  {
    try
    {
      return task.TryGetResult(out var result)
               ? new ValueTask<TOut>(continuation(result))
               : Core(task,
                      continuation);
    }
    catch (OperationCanceledException ex)
    {
      return FromCanceled<TOut>(ex.CancellationToken);
    }
    catch (Exception ex)
    {
      return FromException<TOut>(ex);
    }

    static async ValueTask<TOut> Core(ValueTask<TIn>  task,
                                      Func<TIn, TOut> continuation)
      => continuation(await task.ConfigureAwait(false));
  }

  /// <summary>
  ///   Creates a continuation that executes asynchronously when the target <see cref="ValueTask{TIn}" /> completes
  ///   successfully.
  /// </summary>
  /// <remarks>
  ///   <para>
  ///     The target <paramref name="task" /> must not be awaited after this call.
  ///     Only the continuation task should be awaited.
  ///   </para>
  ///   <para>
  ///     If the <paramref name="task" /> is synchronously completed and fails or the continuation throws,
  ///     the call does not throw and returns a failed <see cref="ValueTask" />.
  ///   </para>
  /// </remarks>
  /// <param name="task">The target task.</param>
  /// <param name="continuation">A function to run when the <paramref name="task" /> completes.</param>
  /// <typeparam name="TIn">The type of the result of the target task.</typeparam>
  /// <returns>The continuation task.</returns>
  [PublicAPI]
  public static ValueTask AndThen<TIn>(this ValueTask<TIn>  task,
                                       Func<TIn, ValueTask> continuation)
  {
    try
    {
      return task.TryGetResult(out var result)
               ? continuation(result)
               : Core(task,
                      continuation);
    }
    catch (OperationCanceledException ex)
    {
      return FromCanceled(ex.CancellationToken);
    }
    catch (Exception ex)
    {
      return FromException(ex);
    }

    static async ValueTask Core(ValueTask<TIn>       task,
                                Func<TIn, ValueTask> continuation)
      => await continuation(await task.ConfigureAwait(false))
           .ConfigureAwait(false);
  }

  /// <summary>
  ///   Creates a continuation that executes asynchronously when the target <see cref="ValueTask{TIn}" /> completes
  ///   successfully and returns a value.
  /// </summary>
  /// <remarks>
  ///   <para>
  ///     The target <paramref name="task" /> must not be awaited after this call.
  ///     Only the continuation task should be awaited.
  ///   </para>
  ///   <para>
  ///     If the <paramref name="task" /> is synchronously completed and fails or the continuation throws,
  ///     the call does not throw and returns a failed <see cref="ValueTask{TOut}" />.
  ///   </para>
  /// </remarks>
  /// <param name="task">The target task.</param>
  /// <param name="continuation">A function to run when the <paramref name="task" /> completes.</param>
  /// <typeparam name="TIn">The type of the result of the target task.</typeparam>
  /// <typeparam name="TOut">The type of the result produced by the continuation.</typeparam>
  /// <returns>The continuation task.</returns>
  [PublicAPI]
  public static ValueTask<TOut> AndThen<TIn, TOut>(this ValueTask<TIn>        task,
                                                   Func<TIn, ValueTask<TOut>> continuation)
  {
    try
    {
      return task.TryGetResult(out var result)
               ? continuation(result)
               : Core(task,
                      continuation);
    }
    catch (OperationCanceledException ex)
    {
      return FromCanceled<TOut>(ex.CancellationToken);
    }
    catch (Exception ex)
    {
      return FromException<TOut>(ex);
    }

    static async ValueTask<TOut> Core(ValueTask<TIn>             task,
                                      Func<TIn, ValueTask<TOut>> continuation)
      => await continuation(await task.ConfigureAwait(false))
           .ConfigureAwait(false);
  }

  /// <summary>
  ///   Synchronously wait a <see cref="ValueTask" />.
  /// </summary>
  /// <param name="task">Task to be awaited</param>
  [PublicAPI]
  public static void WaitSync(this ValueTask task)
  {
    if (!task.TryGetResult())
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
    => task.TryGetResult(out var result)
         ? result
         // Not already completed `ValueTask` cannot be safely synchronously waited in a direct way.
         // Converting to actual `Task` enable to wait for it safely.
         : task.AsTask()
               .GetAwaiter()
               .GetResult();

  /// <summary>
  ///   Check if the <paramref name="task" /> is completed.
  ///   If the task is canceled or faulted, it will throw the according exception.
  /// </summary>
  /// <param name="task"><see cref="ValueTask" /> to check.</param>
  /// <returns>Whether the task has completed.</returns>
  [PublicAPI]
  public static bool TryGetResult(this ValueTask task)
  {
    if (task.IsCompleted)
    {
      // Ensure exception is propagated without wrapping it into an AggregateException like .Result or .Wait() would.
      task.GetAwaiter()
          .GetResult();
      return true;
    }

    return false;
  }

  /// <summary>
  ///   Check if the <paramref name="task" /> is completed and get its result.
  ///   If the task is canceled or faulted, it will throw the according exception.
  /// </summary>
  /// <param name="task"><see cref="ValueTask{TOut}" /> to check.</param>
  /// <param name="result">The result of the <paramref name="task" />.</param>
  /// <typeparam name="TOut">Type of the result of the task.</typeparam>
  /// <returns>Whether the task has completed.</returns>
  [PublicAPI]
  [ContractAnnotation("=> false, result: null; => true, result:notnull")]
  public static bool TryGetResult<TOut>(this ValueTask<TOut> task,
                                        out  TOut            result)
  {
    if (task.IsCompleted)
    {
      // Ensure exception is propagated without wrapping it into an AggregateException like .Result or .Wait() would.
      result = task.GetAwaiter()
                   .GetResult();
      return true;
    }

#pragma warning disable CS8601 // Possible null reference assignment.
    result = default;
#pragma warning restore CS8601 // Possible null reference assignment.

    return false;
  }
}

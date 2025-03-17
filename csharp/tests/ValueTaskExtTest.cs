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
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

using NUnit.Framework;

namespace ArmoniK.Utils.Tests;

public class ValueTaskExtTest
{
  public enum CompletedTaskStatus
  {
    RanToCompletion,
    Failed,
    Canceled,
  }

  private readonly CancellationToken canceled_ = new(true);

  [Test]
  public void CompletedTask()
  {
    var task = ValueTaskExt.CompletedTask;

    Assert.That(task.IsCompletedSuccessfully,
                Is.True);
  }

  [Test]
  public void FromResult()
  {
    var task = ValueTaskExt.FromResult(1);

    Assert.That(task.IsCompletedSuccessfully,
                Is.True);
    Assert.That(() => task,
                Is.EqualTo(1));
  }

  [Test]
  public void FromExceptionV()
  {
    var task = ValueTaskExt.FromException(new ApplicationException());
    Assert.That(task.IsFaulted,
                Is.True);
    Assert.That(() => task,
                Throws.InstanceOf<ApplicationException>());
  }

  [Test]
  public void FromExceptionT()
  {
    var task = ValueTaskExt.FromException<int>(new ApplicationException());
    Assert.That(task.IsFaulted,
                Is.True);
    Assert.That(() => task,
                Throws.InstanceOf<ApplicationException>());
  }

  [Test]
  public void FromCanceledV()
  {
    // ReSharper disable once SuggestVarOrType_SimpleTypes
    ValueTask task = ValueTaskExt.FromCanceled(canceled_);
    Assert.That(task.IsCanceled,
                Is.True);
    Assert.That(() => task,
                Throws.InstanceOf<OperationCanceledException>()
                      .And.Property(nameof(OperationCanceledException.CancellationToken))
                      .EqualTo(canceled_));
  }

  [Test]
  public void FromCanceledT()
  {
    // ReSharper disable once SuggestVarOrType_Elsewhere
    ValueTask<int> task = ValueTaskExt.FromCanceled<int>(canceled_);
    Assert.That(task.IsCanceled,
                Is.True);
    Assert.That(() => task,
                Throws.InstanceOf<OperationCanceledException>()
                      .And.Property(nameof(OperationCanceledException.CancellationToken))
                      .EqualTo(canceled_));
  }

  [Test]
  [Timeout(1000)]
  [Repeat(10)]
  [SuppressMessage("ReSharper",
                   "SuggestVarOrType_SimpleTypes")]
  [SuppressMessage("ReSharper",
                   "InconsistentNaming")]
  public void AndThenVV([Values] bool                asyncTask,
                        [Values] bool?               asyncContinuation,
                        [Values] CompletedTaskStatus taskStatus,
                        [Values] CompletedTaskStatus continuationStatus)
  {
    var tcs       = new TaskCompletionSource<ValueTuple>();
    var continued = false;

    ValueTask sourceTask = ValueTaskFactory(asyncTask
                                              ? tcs.Task
                                              : null,
                                            taskStatus);
    ValueTask task = asyncContinuation is null
                       ? sourceTask.AndThen(() => ValueTaskFactory(null,
                                                                   continuationStatus,
                                                                   () => continued = true)
                                              .WaitSync())
                       : sourceTask.AndThen(() => ValueTaskFactory(asyncContinuation.Value
                                                                     ? tcs.Task
                                                                     : null,
                                                                   continuationStatus,
                                                                   () => continued = true));

    var isAsync = asyncTask || (taskStatus is CompletedTaskStatus.RanToCompletion && (asyncContinuation ?? false));
    var status = taskStatus is CompletedTaskStatus.RanToCompletion
                   ? continuationStatus
                   : taskStatus;

    Assert.That(task.IsCompleted,
                Is.EqualTo(!isAsync));

    tcs.SetResult(new ValueTuple());

    switch (status)
    {
      case CompletedTaskStatus.RanToCompletion:
        Assert.That(() => task,
                    Throws.Nothing);
        break;
      case CompletedTaskStatus.Failed:
        Assert.That(() => task,
                    Throws.InstanceOf<ApplicationException>());
        break;
      case CompletedTaskStatus.Canceled:
        Assert.That(() => task,
                    Throws.InstanceOf<OperationCanceledException>()
                          .And.Property(nameof(OperationCanceledException.CancellationToken))
                          .EqualTo(canceled_));
        break;
    }

    Assert.That(continued,
                Is.EqualTo(taskStatus == CompletedTaskStatus.RanToCompletion));
  }

  [Test]
  [Timeout(1000)]
  [Repeat(10)]
  [SuppressMessage("ReSharper",
                   "SuggestVarOrType_SimpleTypes")]
  [SuppressMessage("ReSharper",
                   "SuggestVarOrType_Elsewhere")]
  [SuppressMessage("ReSharper",
                   "InconsistentNaming")]
  public void AndThenVT([Values] bool                asyncTask,
                        [Values] bool?               asyncContinuation,
                        [Values] CompletedTaskStatus taskStatus,
                        [Values] CompletedTaskStatus continuationStatus)
  {
    var tcs       = new TaskCompletionSource<ValueTuple>();
    var continued = false;

    ValueTask sourceTask = ValueTaskFactory(asyncTask
                                              ? tcs.Task
                                              : null,
                                            taskStatus);
    ValueTask<int> task = asyncContinuation is null
                            ? sourceTask.AndThen(() => ValueTaskFactory(null,
                                                                        continuationStatus,
                                                                        1,
                                                                        () => continued = true)
                                                   .WaitSync())
                            : sourceTask.AndThen(() => ValueTaskFactory(asyncContinuation.Value
                                                                          ? tcs.Task
                                                                          : null,
                                                                        continuationStatus,
                                                                        1,
                                                                        () => continued = true));

    var isAsync = asyncTask || (taskStatus is CompletedTaskStatus.RanToCompletion && (asyncContinuation ?? false));
    var status = taskStatus is CompletedTaskStatus.RanToCompletion
                   ? continuationStatus
                   : taskStatus;

    Assert.That(task.IsCompleted,
                Is.EqualTo(!isAsync));

    tcs.SetResult(new ValueTuple());

    switch (status)
    {
      case CompletedTaskStatus.RanToCompletion:
        Assert.That(() => task,
                    Is.EqualTo(1));
        break;
      case CompletedTaskStatus.Failed:
        Assert.That(() => task,
                    Throws.InstanceOf<ApplicationException>());
        break;
      case CompletedTaskStatus.Canceled:
        Assert.That(() => task,
                    Throws.InstanceOf<OperationCanceledException>()
                          .And.Property(nameof(OperationCanceledException.CancellationToken))
                          .EqualTo(canceled_));
        break;
    }

    Assert.That(continued,
                Is.EqualTo(taskStatus == CompletedTaskStatus.RanToCompletion));
  }

  [Test]
  [Timeout(1000)]
  [Repeat(10)]
  [SuppressMessage("ReSharper",
                   "SuggestVarOrType_SimpleTypes")]
  [SuppressMessage("ReSharper",
                   "SuggestVarOrType_Elsewhere")]
  [SuppressMessage("ReSharper",
                   "InconsistentNaming")]
  public void AndThenTV([Values] bool                asyncTask,
                        [Values] bool?               asyncContinuation,
                        [Values] CompletedTaskStatus taskStatus,
                        [Values] CompletedTaskStatus continuationStatus)
  {
    var tcs       = new TaskCompletionSource<ValueTuple>();
    var continued = false;

    ValueTask<int> sourceTask = ValueTaskFactory(asyncTask
                                                   ? tcs.Task
                                                   : null,
                                                 taskStatus,
                                                 1);
    ValueTask task = asyncContinuation is null
                       ? sourceTask.AndThen(_ => ValueTaskFactory(null,
                                                                  continuationStatus,
                                                                  () => continued = true)
                                              .WaitSync())
                       : sourceTask.AndThen(_ => ValueTaskFactory(asyncContinuation.Value
                                                                    ? tcs.Task
                                                                    : null,
                                                                  continuationStatus,
                                                                  () => continued = true));

    var isAsync = asyncTask || (taskStatus is CompletedTaskStatus.RanToCompletion && (asyncContinuation ?? false));
    var status = taskStatus is CompletedTaskStatus.RanToCompletion
                   ? continuationStatus
                   : taskStatus;

    Assert.That(task.IsCompleted,
                Is.EqualTo(!isAsync));

    tcs.SetResult(new ValueTuple());

    switch (status)
    {
      case CompletedTaskStatus.RanToCompletion:
        Assert.That(() => task,
                    Throws.Nothing);
        break;
      case CompletedTaskStatus.Failed:
        Assert.That(() => task,
                    Throws.InstanceOf<ApplicationException>());
        break;
      case CompletedTaskStatus.Canceled:
        Assert.That(() => task,
                    Throws.InstanceOf<OperationCanceledException>()
                          .And.Property(nameof(OperationCanceledException.CancellationToken))
                          .EqualTo(canceled_));
        break;
    }

    Assert.That(continued,
                Is.EqualTo(taskStatus == CompletedTaskStatus.RanToCompletion));
  }

  [Test]
  [Timeout(1000)]
  [Repeat(10)]
  [SuppressMessage("ReSharper",
                   "SuggestVarOrType_Elsewhere")]
  [SuppressMessage("ReSharper",
                   "InconsistentNaming")]
  public void AndThenTT([Values] bool                asyncTask,
                        [Values] bool?               asyncContinuation,
                        [Values] CompletedTaskStatus taskStatus,
                        [Values] CompletedTaskStatus continuationStatus)
  {
    var tcs       = new TaskCompletionSource<ValueTuple>();
    var continued = false;

    ValueTask<int> sourceTask = ValueTaskFactory(asyncTask
                                                   ? tcs.Task
                                                   : null,
                                                 taskStatus,
                                                 1);
    ValueTask<int> task = asyncContinuation is null
                            ? sourceTask.AndThen(x => ValueTaskFactory(null,
                                                                       continuationStatus,
                                                                       x,
                                                                       () => continued = true)
                                                   .WaitSync())
                            : sourceTask.AndThen(x => ValueTaskFactory(asyncContinuation.Value
                                                                         ? tcs.Task
                                                                         : null,
                                                                       continuationStatus,
                                                                       x,
                                                                       () => continued = true));

    var isAsync = asyncTask || (taskStatus is CompletedTaskStatus.RanToCompletion && (asyncContinuation ?? false));
    var status = taskStatus is CompletedTaskStatus.RanToCompletion
                   ? continuationStatus
                   : taskStatus;

    Assert.That(task.IsCompleted,
                Is.EqualTo(!isAsync));

    tcs.SetResult(new ValueTuple());

    switch (status)
    {
      case CompletedTaskStatus.RanToCompletion:
        Assert.That(() => task,
                    Throws.Nothing);
        break;
      case CompletedTaskStatus.Failed:
        Assert.That(() => task,
                    Throws.InstanceOf<ApplicationException>());
        break;
      case CompletedTaskStatus.Canceled:
        Assert.That(() => task,
                    Throws.InstanceOf<OperationCanceledException>()
                          .And.Property(nameof(OperationCanceledException.CancellationToken))
                          .EqualTo(canceled_));
        break;
    }

    Assert.That(continued,
                Is.EqualTo(taskStatus == CompletedTaskStatus.RanToCompletion));
  }


  private async ValueTask ValueTaskFactory(Task?               waitFor,
                                           CompletedTaskStatus status,
                                           Action?             body = null)
  {
    body?.Invoke();

    if (waitFor is not null)
    {
      await waitFor;
    }

    switch (status)
    {
      case CompletedTaskStatus.RanToCompletion:
        break;
      case CompletedTaskStatus.Failed:
        throw new ApplicationException();
      case CompletedTaskStatus.Canceled:
        canceled_.ThrowIfCancellationRequested();
        break;
    }
  }

  private async ValueTask<T> ValueTaskFactory<T>(Task?               waitFor,
                                                 CompletedTaskStatus status,
                                                 T                   value,
                                                 Action?             body = null)
  {
    body?.Invoke();

    if (waitFor is not null)
    {
      await waitFor;
    }

    switch (status)
    {
      case CompletedTaskStatus.RanToCompletion:
        break;
      case CompletedTaskStatus.Failed:
        throw new ApplicationException();
      case CompletedTaskStatus.Canceled:
        canceled_.ThrowIfCancellationRequested();
        break;
    }

    return value;
  }


  [Test]
  [Timeout(1000)]
  [Repeat(10)]
  public void WaitSyncValueTask()
  {
    var task = new ValueTask<int>(new Source<int>(1),
                                  0);

    var x = task.WaitSync();
    Assert.That(x,
                Is.EqualTo(1));
  }

  [Test]
  [Timeout(1000)]
  [Repeat(10)]
  public void WaitSyncValueTaskUntyped()
  {
    var task = new ValueTask(new Source<int>(1),
                             0);

    task.WaitSync();
  }

  [Test]
  [Timeout(1000)]
  [Repeat(10)]
  public void WaitSyncTask()
  {
    var task = new ValueTask<int>(new Source<int>(1),
                                  0).AsTask();

    var x = task.WaitSync();
    Assert.That(x,
                Is.EqualTo(1));
  }

  [Test]
  [Timeout(1000)]
  [Repeat(10)]
  public void WaitSyncTaskUntyped()
  {
    var task = new ValueTask(new Source<int>(1),
                             0).AsTask();

    task.WaitSync();
  }

  [Test]
  [Timeout(1000)]
  [Repeat(10)]
  public void TryGetSyncPendingUntyped([Values] CompletedTaskStatus status)
  {
    var tcs = new TaskCompletionSource<ValueTuple>();

    var task = ValueTaskFactory(tcs.Task,
                                status);

    Assert.That(task.TryGetSync(),
                Is.False);

    tcs.SetCanceled();
  }

  [Test]
  [Timeout(1000)]
  [Repeat(10)]
  public void TryGetSyncPending([Values] CompletedTaskStatus status)
  {
    var tcs = new TaskCompletionSource<ValueTuple>();

    var task = ValueTaskFactory(tcs.Task,
                                status,
                                new object());

    Assert.That(task.TryGetSync(out var result),
                Is.False);
    Assert.That(result,
                Is.Null);

    tcs.SetCanceled();
  }

  [Test]
  [Timeout(1000)]
  [Repeat(10)]
  public void TryGetSyncCompletedUntyped()
  {
    var called = false;
    var task = ValueTaskFactory(null,
                                CompletedTaskStatus.RanToCompletion,
                                () => called = true);

    Assert.That(task.TryGetSync(),
                Is.True);
    Assert.That(called,
                Is.True);
  }

  [Test]
  [Timeout(1000)]
  [Repeat(10)]
  public void TryGetSyncCompleted()
  {
    var called = false;
    var task = ValueTaskFactory(null,
                                CompletedTaskStatus.RanToCompletion,
                                1,
                                () => called = true);

    Assert.That(task.TryGetSync(out var result),
                Is.True);
    Assert.That(result,
                Is.EqualTo(1));
    Assert.That(called,
                Is.True);
  }

  [Test]
  [Timeout(1000)]
  [Repeat(10)]
  public void TryGetSyncFailedUntyped()
  {
    var called = false;
    var task = ValueTaskFactory(null,
                                CompletedTaskStatus.Failed,
                                () => called = true);

    Assert.That(() => task.TryGetSync(),
                Throws.InstanceOf<ApplicationException>());
    Assert.That(called,
                Is.True);
  }

  [Test]
  [Timeout(1000)]
  [Repeat(10)]
  public void TryGetSyncFailed()
  {
    var called = false;
    var task = ValueTaskFactory(null,
                                CompletedTaskStatus.Failed,
                                1,
                                () => called = true);

    Assert.That(() => task.TryGetSync(out _),
                Throws.InstanceOf<ApplicationException>());
    Assert.That(called,
                Is.True);
  }

  [Test]
  [Timeout(1000)]
  [Repeat(10)]
  public void TryGetSyncCanceledUntyped()
  {
    var called = false;
    var task = ValueTaskFactory(null,
                                CompletedTaskStatus.Canceled,
                                () => called = true);

    Assert.That(() => task.TryGetSync(),
                Throws.InstanceOf<OperationCanceledException>()
                      .And.Property(nameof(OperationCanceledException.CancellationToken))
                      .EqualTo(canceled_));
    Assert.That(called,
                Is.True);
  }

  [Test]
  [Timeout(1000)]
  [Repeat(10)]
  public void TryGetCanceledFailed()
  {
    var called = false;
    var task = ValueTaskFactory(null,
                                CompletedTaskStatus.Canceled,
                                1,
                                () => called = true);

    Assert.That(() => task.TryGetSync(out _),
                Throws.InstanceOf<OperationCanceledException>()
                      .And.Property(nameof(OperationCanceledException.CancellationToken))
                      .EqualTo(canceled_));
    Assert.That(called,
                Is.True);
  }

  private class Source<T> : IValueTaskSource, IValueTaskSource<T>
  {
    private readonly Task<T> task_;

    public Source(T val)
    {
      async Task<T> Dummy()
      {
        await Task.Delay(10)
                  .ConfigureAwait(false);
        return val;
      }

      task_ = Dummy();
    }

    public ValueTaskSourceStatus GetStatus(short token)
      => task_.Status switch
         {
           TaskStatus.Created                      => ValueTaskSourceStatus.Pending,
           TaskStatus.WaitingForActivation         => ValueTaskSourceStatus.Pending,
           TaskStatus.WaitingToRun                 => ValueTaskSourceStatus.Pending,
           TaskStatus.Running                      => ValueTaskSourceStatus.Pending,
           TaskStatus.WaitingForChildrenToComplete => ValueTaskSourceStatus.Pending,
           TaskStatus.RanToCompletion              => ValueTaskSourceStatus.Succeeded,
           TaskStatus.Canceled                     => ValueTaskSourceStatus.Canceled,
           TaskStatus.Faulted                      => ValueTaskSourceStatus.Faulted,
           _                                       => throw new ArgumentOutOfRangeException(),
         };

    public void OnCompleted(Action<object?>                 continuation,
                            object?                         state,
                            short                           token,
                            ValueTaskSourceOnCompletedFlags flags)
      => Task.Factory.StartNew(async () =>
                               {
                                 await task_.ConfigureAwait(false);
                                 continuation(state);
                               },
                               CancellationToken.None,
                               TaskCreationOptions.LongRunning,
                               TaskScheduler.Current);

    void IValueTaskSource.GetResult(short token)
      => _ = GetResult(token);

    public T GetResult(short token)
      => task_.IsCompleted
           ? task_.Result
           : throw new ApplicationException();
  }
}

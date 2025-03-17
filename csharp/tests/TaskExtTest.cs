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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

namespace ArmoniK.Utils.Tests;

public class TaskExtTest
{
  public enum CompletedTaskStatus
  {
    RanToCompletion,
    Failed,
    Canceled,
  }

  private readonly CancellationToken canceled_ = new(true);

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

    Task sourceTask = TaskFactory(asyncTask
                                    ? tcs.Task
                                    : null,
                                  taskStatus);
    Task task = asyncContinuation is null
                  ? sourceTask.AndThen(() => TaskFactory(null,
                                                         continuationStatus,
                                                         () => continued = true)
                                         .WaitSync())
                  : sourceTask.AndThen(() => TaskFactory(asyncContinuation.Value
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

    Task sourceTask = TaskFactory(asyncTask
                                    ? tcs.Task
                                    : null,
                                  taskStatus);
    Task<int> task = asyncContinuation is null
                       ? sourceTask.AndThen(() => TaskFactory(null,
                                                              continuationStatus,
                                                              1,
                                                              () => continued = true)
                                              .WaitSync())
                       : sourceTask.AndThen(() => TaskFactory(asyncContinuation.Value
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

    Task<int> sourceTask = TaskFactory(asyncTask
                                         ? tcs.Task
                                         : null,
                                       taskStatus,
                                       1);
    Task task = asyncContinuation is null
                  ? sourceTask.AndThen(_ => TaskFactory(null,
                                                        continuationStatus,
                                                        () => continued = true)
                                         .WaitSync())
                  : sourceTask.AndThen(_ => TaskFactory(asyncContinuation.Value
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

    Task<int> sourceTask = TaskFactory(asyncTask
                                         ? tcs.Task
                                         : null,
                                       taskStatus,
                                       1);
    Task<int> task = asyncContinuation is null
                       ? sourceTask.AndThen(x => TaskFactory(null,
                                                             continuationStatus,
                                                             x,
                                                             () => continued = true)
                                              .WaitSync())
                       : sourceTask.AndThen(x => TaskFactory(asyncContinuation.Value
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


  private async Task TaskFactory(Task?               waitFor,
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

  private async Task<T> TaskFactory<T>(Task?               waitFor,
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
  [Repeat(3)]
  [TestCase(0)]
  [TestCase(1)]
  [TestCase(5)]
  public async Task WhenAllShouldWork(int n)
  {
    var tasks = Enumerable.Range(0,
                                 n)
                          .Select(_ => Task.Delay(100,
                                                  CancellationToken.None))
                          .ToList();
    var allTask = tasks.WhenAll();

    foreach (var task in tasks)
    {
      Assert.That(task.IsCompleted,
                  Is.False);
    }

    await allTask;

    foreach (var task in tasks)
    {
      Assert.That(task.IsCompleted,
                  Is.True);
    }
  }

  [Test]
  [Timeout(1000)]
  [Repeat(3)]
  [TestCase(0)]
  [TestCase(1)]
  [TestCase(5)]
  public async Task WhenAllTypedShouldWork(int n)
  {
    var tasks = Enumerable.Range(0,
                                 n)
                          .Select(async i =>
                                  {
                                    await Task.Delay(100,
                                                     CancellationToken.None);
                                    return i;
                                  })
                          .ToList();
    var allTask = tasks.WhenAll();

    foreach (var task in tasks)
    {
      Assert.That(task.IsCompleted,
                  Is.False);
    }

    var results = await allTask;

    foreach (var task in tasks)
    {
      Assert.That(task.IsCompleted,
                  Is.True);
    }

    for (var i = 0; i < n; ++i)
    {
      Assert.That(results[i],
                  Is.EqualTo(i));
    }
  }

  [Test]
  [Timeout(1000)]
  [Repeat(3)]
  [TestCase(0)]
  [TestCase(1)]
  [TestCase(5)]
  public async Task ToListAsyncShouldWork(int n)
  {
    async Task<IEnumerable<int>> Gen()
    {
      await Task.Delay(100,
                       CancellationToken.None);
      return Enumerable.Range(0,
                              n);
    }

    var task = Gen()
      .ToListAsync();
    Assert.That(task.IsCompleted,
                Is.False);

    var results = await task;

    for (var i = 0; i < n; ++i)
    {
      Assert.That(results[i],
                  Is.EqualTo(i));
    }
  }

  [Test]
  [Timeout(1000)]
  [Repeat(10)]
  public void TryGetSyncPendingUntyped([Values] CompletedTaskStatus status)
  {
    var tcs = new TaskCompletionSource<ValueTuple>();

    var task = TaskFactory(tcs.Task,
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

    var task = TaskFactory(tcs.Task,
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
    var task = TaskFactory(null,
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
    var task = TaskFactory(null,
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
    var task = TaskFactory(null,
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
    var task = TaskFactory(null,
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
    var task = TaskFactory(null,
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
  public void TryGetSyncCanceled()
  {
    var called = false;
    var task = TaskFactory(null,
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
}

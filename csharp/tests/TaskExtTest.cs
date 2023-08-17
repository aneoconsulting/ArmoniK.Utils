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
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

using NUnit.Framework;

namespace ArmoniK.Utils.Tests;

public class TaskExtTest
{
  [Test]
  [TestCase(0)]
  [TestCase(1)]
  [TestCase(5)]
  public async Task WhenAllShouldWork(int n)
  {
    var tasks = Enumerable.Range(0,
                                 n)
                          .Select(_ => Task.Delay(100))
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
  [TestCase(0)]
  [TestCase(1)]
  [TestCase(5)]
  public async Task WhenAllTypedShouldWork(int n)
  {
    var tasks = Enumerable.Range(0,
                                 n)
                          .Select(async i =>
                                  {
                                    await Task.Delay(100);
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
  [TestCase(0)]
  [TestCase(1)]
  [TestCase(5)]
  public async Task ToListAsyncShouldWork(int n)
  {
    async Task<IEnumerable<int>> Gen()
    {
      await Task.Delay(100);
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
  public void WaitSyncValueTask()
  {
    var task = new ValueTask<int>(new Source<int>(1),
                                  0);

    var x = task.WaitSync();
    Assert.That(x,
                Is.EqualTo(1));
  }

  [Test]
  public void WaitSyncValueTaskUntyped()
  {
    var task = new ValueTask(new Source<int>(1),
                             0);

    task.WaitSync();
  }

  [Test]
  public void WaitSyncTask()
  {
    var task = new ValueTask<int>(new Source<int>(1),
                                  0).AsTask();

    var x = task.WaitSync();
    Assert.That(x,
                Is.EqualTo(1));
  }

  [Test]
  public void WaitSyncTaskUntyped()
  {
    var task = new ValueTask(new Source<int>(1),
                             0).AsTask();

    task.WaitSync();
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

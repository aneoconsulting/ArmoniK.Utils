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
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

using NUnit.Framework;

namespace ArmoniK.Utils.Tests;

public class ValueTaskExtTest
{
  [Test]
  public void Foo()
  {
    var cts = new CancellationTokenSource();
    cts.Cancel();
    var task = Bar(cts.Token);

    Assert.That(task.IsCanceled,
                Is.True);

    static async ValueTask Bar(CancellationToken cancellation)
    {
      throw new OperationCanceledException(cancellation);
      await Task.Yield();
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

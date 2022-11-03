// This file is part of the ArmoniK project
//
// Copyright (C) ANEO, 2022-2022. All rights reserved.
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

using NUnit.Framework;

namespace ArmoniK.Utils.Tests;

[TestFixture(TestOf = typeof(ExecutionSingleizer<int>))]
public class ExecutionSingleizerTest
{
  [SetUp]
  public void SetUp()
  {
    single_ = new ExecutionSingleizer<int>();
    val_    = 0;
  }

  [TearDown]
  public void TearDown()
    => single_ = null;

  private ExecutionSingleizer<int>? single_;
  private int                       val_;

  [Test]
  public async Task SingleExecutionShouldSucceed()
  {
    var i = await single_!.Call(ct => Set(1,
                                          0,
                                          ct))
                          .ConfigureAwait(false);
    Assert.That(i,
                Is.EqualTo(1));
    Assert.That(val_,
                Is.EqualTo(1));
  }

  [Test]
  public async Task RepeatedExecutionShouldSucceed()
  {
    for (var t = 1; t <= 10; ++t)
    {
      var t2 = t;
      var i = await single_!.Call(ct => Set(t2,
                                            0,
                                            ct))
                            .ConfigureAwait(false);
      Assert.That(i,
                  Is.EqualTo(t));
      Assert.That(val_,
                  Is.EqualTo(t));
    }
  }

  [Test]
  public async Task ConcurrentExecutionShouldSucceed()
  {
    var ti = single_!.Call(ct => Set(1,
                                     10,
                                     ct));
    var tj = single_!.Call(ct => Set(2,
                                     10,
                                     ct));
    var i = await ti.ConfigureAwait(false);
    var j = await tj.ConfigureAwait(false);
    Assert.That(i,
                Is.EqualTo(1));
    Assert.That(j,
                Is.EqualTo(1));
    Assert.That(val_,
                Is.EqualTo(1));
  }

  [Test]
  public async Task RepeatedConcurrentExecutionShouldSucceed()
  {
    for (var t = 1; t <= 10 * 2; t += 2)
    {
      var t2 = t;
      var ti = single_!.Call(ct => Set(t2,
                                       10,
                                       ct));
      var tj = single_!.Call(ct => Set(t2 + 1,
                                       10,
                                       ct));
      var i = await ti.ConfigureAwait(false);
      var j = await tj.ConfigureAwait(false);
      Assert.That(i,
                  Is.EqualTo(t));
      Assert.That(j,
                  Is.EqualTo(t));
      Assert.That(val_,
                  Is.EqualTo(t));
    }
  }

  [Test]
  public async Task ManyConcurrentExecutionShouldSucceed()
  {
    var n     = 1000000;
    var tasks = new Task<int>[n];

    for (var i = 0; i < n; ++i)
    {
      var i2 = i;
      tasks[i] = single_!.Call(ct => Set(i2,
                                         0,
                                         ct));
    }

    for (var i = 0; i < n; ++i)
    {
      var j = await tasks[i]
                .ConfigureAwait(false);
      Assert.GreaterOrEqual(i,
                            j);
    }
  }

  [Test]
  public void CancelExecutionShouldFail()
  {
    Assert.ThrowsAsync<TaskCanceledException>(async () =>
                                              {
                                                var cts = new CancellationTokenSource();
                                                var task = single_!.Call(ct => Set(1,
                                                                                   1000,
                                                                                   ct),
                                                                         cts.Token);
                                                cts.Cancel();
                                                await task.ConfigureAwait(false);
                                              });
    Assert.That(val_,
                Is.EqualTo(0));
    Assert.ThrowsAsync<TaskCanceledException>(async () =>
                                              {
                                                var cts = new CancellationTokenSource();
                                                cts.Cancel();
                                                var task = single_!.Call(ct => Set(1,
                                                                                   1000,
                                                                                   ct),
                                                                         cts.Token);
                                                await task.ConfigureAwait(false);
                                              });
    Assert.That(val_,
                Is.EqualTo(0));
  }

  [Test]
  public async Task ConcurrentPartialCancelExecutionShouldSucceed()
  {
    var cts = new CancellationTokenSource();
    var t1 = single_!.Call(ct => Set(1,
                                     100,
                                     ct),
                           cts.Token);
    var t2 = single_!.Call(ct => Set(2,
                                     100,
                                     ct),
                           CancellationToken.None);
    cts.Cancel();
    Assert.ThrowsAsync<TaskCanceledException>(async () => await t1.ConfigureAwait(false));

    var j = await t2.ConfigureAwait(false);
    Assert.That(j,
                Is.EqualTo(1));
    Assert.That(val_,
                Is.EqualTo(1));
  }

  [Test]
  public void ConcurrentCancelExecutionShouldFail()
  {
    var cts = new CancellationTokenSource();
    var t1 = single_!.Call(ct => Set(1,
                                     1000,
                                     ct),
                           cts.Token);
    var t2 = single_!.Call(ct => Set(2,
                                     1000,
                                     ct),
                           cts.Token);
    cts.Cancel();
    Assert.ThrowsAsync<TaskCanceledException>(async () => await t1.ConfigureAwait(false));
    Assert.ThrowsAsync<TaskCanceledException>(async () => await t2.ConfigureAwait(false));

    Assert.That(val_,
                Is.EqualTo(0));
  }

  private async Task<int> Set(int               i,
                              int               delay,
                              CancellationToken cancellationToken)
  {
    if (delay > 0)
    {
      await Task.Delay(delay,
                       cancellationToken)
                .ConfigureAwait(false);
    }

    val_ = i;
    return i;
  }
}

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

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

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
    var n = 10000000;

    var tasks = Enumerable.Range(0,
                                 n)
                          .Select(i => single_!.Call(ct => Set(i,
                                                               0,
                                                               ct)))
                          .ToList();
    var results = await Task.WhenAll(tasks)
                            .ConfigureAwait(false);

    for (var i = 0; i < n; ++i)
    {
      var j = results[i];
      Assert.GreaterOrEqual(i,
                            j);
    }
  }


  [Test]
  public void ManyThreadedConcurrentExecutionShouldSucceed()
  {
    var n = 10000000;

    var tasks = Enumerable.Range(0,
                                 n)
                          .AsParallel()
                          .WithExecutionMode(ParallelExecutionMode.ForceParallelism)
                          .Select(i => single_!.Call(ct => Set(i,
                                                               0,
                                                               ct)))
                          .ToList();
    var results = tasks.AsParallel()
                       .Select(task => task.Result);

    Assert.That(results,
                Is.All.InRange(0,
                               n));
  }

  [Test]
  public void CancelExecutionShouldFail()
  {
    Assert.That(async () =>
                {
                  var cts = new CancellationTokenSource();
                  var task = single_!.Call(ct => Set(1,
                                                     1000,
                                                     ct),
                                           cts.Token);
                  cts.Cancel();
                  await task.ConfigureAwait(false);
                },
                Throws.InstanceOf<OperationCanceledException>());
    Assert.That(val_,
                Is.EqualTo(0));
    Assert.That(async () =>
                {
                  var cts = new CancellationTokenSource();
                  cts.Cancel();
                  var task = single_!.Call(ct => Set(1,
                                                     1000,
                                                     ct),
                                           cts.Token);
                  await task.ConfigureAwait(false);
                },
                Throws.InstanceOf<OperationCanceledException>());
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
    Assert.That(() => t1,
                Throws.InstanceOf<OperationCanceledException>());

    var j = await t2.ConfigureAwait(false);
    Assert.That(j,
                Is.EqualTo(1));
    Assert.That(val_,
                Is.EqualTo(1));
  }

  [Test]
  public async Task ConcurrentCancelExecutionShouldFail()
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
    await Task.Delay(10,
                     CancellationToken.None)
              .ConfigureAwait(false);

    cts.Cancel();
    Assert.That(() => t1,
                Throws.InstanceOf<OperationCanceledException>());
    Assert.That(() => t2,
                Throws.InstanceOf<OperationCanceledException>());

    Assert.That(val_,
                Is.EqualTo(0));
  }

  [Test]
  public async Task CheckExpire()
  {
    var single = new ExecutionSingleizer<int>(TimeSpan.FromMilliseconds(100));
    var i = await single.Call(ct => Set(1,
                                        0,
                                        ct))
                        .ConfigureAwait(false);
    Assert.That(i,
                Is.EqualTo(1));
    Assert.That(val_,
                Is.EqualTo(1));

    i = await single.Call(ct => Set(2,
                                    0,
                                    ct))
                    .ConfigureAwait(false);
    Assert.That(i,
                Is.EqualTo(1));
    Assert.That(val_,
                Is.EqualTo(1));

    await Task.Delay(150)
              .ConfigureAwait(false);

    i = await single.Call(ct => Set(3,
                                    0,
                                    ct))
                    .ConfigureAwait(false);
    Assert.That(i,
                Is.EqualTo(3));
    Assert.That(val_,
                Is.EqualTo(3));
  }

  private async Task<int> Set(int               i,
                              int               delay,
                              CancellationToken cancellationToken)
  {
    Assert.That(cancellationToken.IsCancellationRequested,
                Is.False);
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

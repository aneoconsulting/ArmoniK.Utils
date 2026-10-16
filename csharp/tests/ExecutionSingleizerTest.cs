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
using System.Diagnostics;
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
  public async Task TearDown()
  {
    if (single_ is not null)
    {
      await single_.Call(_ => Task.FromResult(0),
                         CancellationToken.None)
                   .ConfigureAwait(false);
      single_.Dispose();
      single_ = null;
    }
  }

  private ExecutionSingleizer<int>? single_;
  private int                       val_;

  [Test]
  [AbortAfter(1000)]
  public async Task SingleExecution()
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
  [AbortAfter(1000)]
  public async Task RepeatedExecution()
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
  [AbortAfter(1000)]
  public async Task ConcurrentExecution()
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
  [AbortAfter(10000)]
  public async Task RepeatedConcurrentExecution()
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
  [AbortAfter(100000)]
  public async Task ManyConcurrentExecution()
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
      Assert.That(j,
                  Is.LessThanOrEqualTo(i));
    }
  }


  [Test]
  [AbortAfter(100000)]
  public void ManyThreadedConcurrentExecution()
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
  [AbortAfter(1000)]
  public void CancelExecution()
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
  [AbortAfter(10000)]
  public async Task ConcurrentPartialCancelExecution()
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
  [AbortAfter(1000)]
  public async Task ConcurrentCancelExecution()
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
  [AbortAfter(10000)]
  public async Task Expire()
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

  [Test]
  [AbortAfter(60000)]
  public async Task ConcurrentCallsOnExpiredHandleKeepRefreshing()
  {
    var ttl    = TimeSpan.FromMilliseconds(20);
    var reads  = 0;
    var single = new ExecutionSingleizer<int>(ttl);

    async Task<int> Read(CancellationToken ct)
    {
      var n = Interlocked.Increment(ref reads);
      await Task.Delay(2,
                       ct)
                .ConfigureAwait(false);
      return n;
    }

    await single.Call(Read)
                .ConfigureAwait(false);

    for (var it = 0; it < 200; it++)
    {
      await Task.Delay(ttl + TimeSpan.FromMilliseconds(5))
                .ConfigureAwait(false);

      // Two callers race to replace the expired handle
      using var go = new Barrier(2);
      var t1 = Task.Run(() =>
                        {
                          go.SignalAndWait();
                          return single.Call(Read);
                        });
      var t2 = Task.Run(() =>
                        {
                          go.SignalAndWait();
                          return single.Call(Read);
                        });
      await Task.WhenAll(t1,
                         t2)
                .ConfigureAwait(false);

      var before = reads;
      await Task.Delay(ttl + TimeSpan.FromMilliseconds(5))
                .ConfigureAwait(false);

      // The result has expired and there is a single caller: func must be called again
      await single.Call(Read)
                  .ConfigureAwait(false);
      Assert.That(reads,
                  Is.GreaterThan(before),
                  $"No refresh after iteration {it}");
    }

    single.Dispose();
  }

  [Test]
  [AbortAfter(10000)]
  public async Task CancellationIsNotCached()
  {
    var single = new ExecutionSingleizer<int>(TimeSpan.FromSeconds(10));

    using var cts     = new CancellationTokenSource();
    var       started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    var t1 = single.Call(async ct =>
                         {
                           started.SetResult(true);
                           await Task.Delay(1000,
                                            ct)
                                     .ConfigureAwait(false);
                           return 1;
                         },
                         cts.Token);
    // Cancel while func is running
    await started.Task.ConfigureAwait(false);
    cts.Cancel();
    Assert.That(() => t1,
                Throws.InstanceOf<OperationCanceledException>());

    // The previous call was cancelled by its only caller: its outcome must not be served
    var i = await single.Call(ct => Set(2,
                                        0,
                                        ct))
                        .ConfigureAwait(false);
    Assert.That(i,
                Is.EqualTo(2));
    Assert.That(val_,
                Is.EqualTo(2));

    single.Dispose();
  }

  [Test]
  [AbortAfter(10000)]
  public async Task FailureIsCached()
  {
    var single = new ExecutionSingleizer<int>(TimeSpan.FromMilliseconds(100));

    Assert.That(() => single.Call(_ => Task.FromException<int>(new ApplicationException())),
                Throws.InstanceOf<ApplicationException>());

    // The failure is kept for the validity period
    Assert.That(() => single.Call(ct => Set(1,
                                            0,
                                            ct)),
                Throws.InstanceOf<ApplicationException>());
    Assert.That(val_,
                Is.EqualTo(0));

    await Task.Delay(150)
              .ConfigureAwait(false);

    var i = await single.Call(ct => Set(2,
                                        0,
                                        ct))
                        .ConfigureAwait(false);
    Assert.That(i,
                Is.EqualTo(2));

    single.Dispose();
  }

  [Test]
  [AbortAfter(10000)]
  public async Task DisposeWhileRunning()
  {
    var single  = new ExecutionSingleizer<int>(TimeSpan.FromSeconds(1));
    var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    var gate    = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    var task = single.Call(async _ =>
                           {
                             started.SetResult(true);
                             await gate.Task.ConfigureAwait(false);
                             return 1;
                           });

    // Dispose while func is running
    await started.Task.ConfigureAwait(false);
    Assert.That(() => single.Dispose(),
                Throws.Nothing);

    gate.SetResult(true);

    // func ignores the cancellation, so the caller still gets its result
    Assert.That(await task.ConfigureAwait(false),
                Is.EqualTo(1));
  }

  [Test]
  [AbortAfter(10000)]
  public async Task DisposeCancelsRunningCall()
  {
    var single  = new ExecutionSingleizer<int>(TimeSpan.FromSeconds(1));
    var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

    async Task<int> Run(CancellationToken ct)
    {
      started.TrySetResult(true);
      await Task.Delay(1000,
                       ct)
                .ConfigureAwait(false);
      val_ = 1;
      return 1;
    }

    var t1 = single.Call(Run);
    var t2 = single.Call(Run);

    // Dispose while func is running
    await started.Task.ConfigureAwait(false);
    Assert.That(() => single.Dispose(),
                Throws.Nothing);

    Assert.That(() => t1,
                Throws.InstanceOf<OperationCanceledException>());
    Assert.That(() => t2,
                Throws.InstanceOf<OperationCanceledException>());
    Assert.That(val_,
                Is.EqualTo(0));
  }

  [Test]
  [AbortAfter(30000)]
  public void ContinuousCallsKeepRefreshing()
  {
    var ttl    = TimeSpan.FromMilliseconds(10);
    var reads  = 0;
    var single = new ExecutionSingleizer<int>(ttl);
    var sw     = Stopwatch.StartNew();

    // More threads than cores, so that callers are preempted in the middle of a call and overlap continuously
    var threads = Enumerable.Range(0,
                                   4 * Environment.ProcessorCount)
                            .Select(_ => new Thread(() =>
                                                    {
                                                      while (sw.ElapsedMilliseconds < 1000)
                                                      {
                                                        single.Call(_ => Task.FromResult(Interlocked.Increment(ref reads)))
                                                              .GetAwaiter()
                                                              .GetResult();
                                                      }
                                                    }))
                            .ToList();
    threads.ForEach(t => t.Start());
    threads.ForEach(t => t.Join());

    // About 100 refreshes are expected in 1 s with a validity of 10 ms
    Assert.That(reads,
                Is.GreaterThan(20));

    single.Dispose();
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

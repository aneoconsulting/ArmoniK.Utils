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
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

using ArmoniK.Utils.Pool;

using NUnit.Framework;
using NUnit.Framework.Constraints;

namespace ArmoniK.Utils.Tests.PoolTests;

[TestFixture(TestOf = typeof(PoolRaw<>))]
public class PoolRawTest
{
  [Test]
  [AbortAfter(1000)]
  [SuppressMessage("ReSharper",
                   "MethodSupportsCancellation")]
  public async Task DefaultValues()
  {
    var x = 0;
    await using var pool = new PoolRaw<int>(new PoolPolicy<int>().SetCreate(() => ++x)
                                                                 .SetValidate(_ => false));

    Assert.That(await pool.AcquireAsync(),
                Is.EqualTo(1));


    Assert.That(await pool.AcquireAsync(),
                Is.EqualTo(2));

    Assert.That(await pool.AcquireAsync(CancellationToken.None),
                Is.EqualTo(3));

    await pool.ReleaseAsync(1);

    // ReSharper disable once RedundantArgumentDefaultValue
    await pool.ReleaseAsync(2,
                            null);

    await pool.ReleaseAsync(3,
                            null,
                            CancellationToken.None);

    Assert.That(await pool.AcquireAsync(),
                Is.EqualTo(4));
  }

  [Test]
  [CancelAfter(1000)]
  [AbortAfter(1500)]
  public async Task AcquireReuse(CancellationToken cancellationTokenTimeout)
  {
    var x    = 0;
    var pool = new PoolRaw<int>(new PoolPolicy<int>().SetCreate(() => ++x));

    Assert.That(await pool.AcquireAsync(cancellationTokenTimeout),
                Is.EqualTo(1));

    await pool.ReleaseAsync(1,
                            null,
                            cancellationTokenTimeout);

    Assert.That(await pool.AcquireAsync(cancellationTokenTimeout),
                Is.EqualTo(1));
    Assert.That(await pool.AcquireAsync(cancellationTokenTimeout),
                Is.EqualTo(2));
    Assert.That(x,
                Is.EqualTo(2));
    cancellationTokenTimeout.ThrowIfCancellationRequested();
  }

  [Test]
  [CancelAfter(1000)]
  [AbortAfter(1500)]
  public async Task AcquireNoReuse(CancellationToken cancellationTokenTimeout)
  {
    var x = 0;
    var pool = new PoolRaw<int>(new PoolPolicy<int>().SetCreate(() => ++x)
                                                     .SetValidate(_ => false));

    Assert.That(await pool.AcquireAsync(cancellationTokenTimeout),
                Is.EqualTo(1));

    await pool.ReleaseAsync(1,
                            null,
                            cancellationTokenTimeout);

    Assert.That(await pool.AcquireAsync(cancellationTokenTimeout),
                Is.EqualTo(2));
    Assert.That(x,
                Is.EqualTo(2));
    cancellationTokenTimeout.ThrowIfCancellationRequested();
  }

  [Test]
  [CancelAfter(1000)]
  [AbortAfter(1500)]
  public async Task AcquireLimit([Values] bool     reuse,
                                 CancellationToken cancellationTokenTimeout)
  {
    var x = 0;
    var pool = new PoolRaw<int>(new PoolPolicy<int>().SetMaxNumberOfInstances(2)
                                                     .SetCreate(() => ++x)
                                                     .SetValidate(_ => reuse));

    Assert.That(await pool.AcquireAsync(cancellationTokenTimeout),
                Is.EqualTo(1));
    Assert.That(await pool.AcquireAsync(cancellationTokenTimeout),
                Is.EqualTo(2));

    var task = pool.AcquireAsync(cancellationTokenTimeout);

    await Task.Yield();

    Assert.That(task.IsCompleted,
                Is.False);

    await pool.ReleaseAsync(1,
                            null,
                            cancellationTokenTimeout);

    AssertThatAsync(task,
                    Is.EqualTo(reuse
                                 ? 1
                                 : 3));
    cancellationTokenTimeout.ThrowIfCancellationRequested();
  }

  [Test]
  [CancelAfter(1000)]
  [AbortAfter(1500)]
  public async Task ValidateAcquire([Values] bool?    asyncDispose,
                                    CancellationToken cancellationTokenTimeout)
  {
    var x = 0;
    var pool = new PoolRaw<object>(new PoolPolicy<object>().SetCreate(() => DisposeAction(asyncDispose,
                                                                                          () => ++x))
                                                           .SetValidateAcquire(_ => false));

    var obj = await pool.AcquireAsync(cancellationTokenTimeout);
    await pool.ReleaseAsync(obj,
                            null,
                            cancellationTokenTimeout);

    Assert.That(x,
                Is.EqualTo(0));

    obj = await pool.AcquireAsync(cancellationTokenTimeout);

    Assert.That(x,
                Is.EqualTo(1));

    await pool.ReleaseAsync(obj,
                            null,
                            cancellationTokenTimeout);

    Assert.That(x,
                Is.EqualTo(1));

    await pool.DisposeAsync();

    Assert.That(x,
                Is.EqualTo(2));
    cancellationTokenTimeout.ThrowIfCancellationRequested();
  }

  [Test]
  [CancelAfter(1000)]
  [AbortAfter(1500)]
  public async Task ValidateRelease([Values] bool?    asyncDispose,
                                    CancellationToken cancellationTokenTimeout)
  {
    var x = 0;
    var pool = new PoolRaw<object>(new PoolPolicy<object>().SetCreate(() => DisposeAction(asyncDispose,
                                                                                          () => ++x))
                                                           .SetValidateRelease(_ => false));

    var obj = await pool.AcquireAsync(cancellationTokenTimeout);
    await pool.ReleaseAsync(obj,
                            null,
                            cancellationTokenTimeout);

    Assert.That(x,
                Is.EqualTo(1));

    await pool.DisposeAsync();

    Assert.That(x,
                Is.EqualTo(1));
    cancellationTokenTimeout.ThrowIfCancellationRequested();
  }

  [Test]
  [CancelAfter(1000)]
  [AbortAfter(1500)]
  public async Task CreateFailure(CancellationToken cancellationTokenTimeout)
  {
    var pool = new PoolRaw<int>(new PoolPolicy<int>().SetCreate(() => ValueTaskExt.FromException<int>(new ApplicationException())));

    AssertThatAsync(pool.AcquireAsync(cancellationTokenTimeout),
                    Throws.InstanceOf<ApplicationException>());

    await pool.ReleaseAsync(1,
                            null,
                            cancellationTokenTimeout);

    Assert.That(await pool.AcquireAsync(cancellationTokenTimeout),
                Is.EqualTo(1));
    cancellationTokenTimeout.ThrowIfCancellationRequested();
  }

  [Test]
  [CancelAfter(1000)]
  [AbortAfter(1500)]
  public async Task ValidateAcquireFailure([Values] bool?    asyncDispose,
                                           CancellationToken cancellationTokenTimeout)
  {
    var x = 0;
    var pool = new PoolRaw<object>(new PoolPolicy<object>().SetMaxNumberOfInstances(1)
                                                           .SetCreate(() => DisposeAction(asyncDispose,
                                                                                          () => ++x))
                                                           .SetValidateAcquire(_ => ValueTaskExt.FromException<bool>(new ApplicationException())));

    var obj = await pool.AcquireAsync(cancellationTokenTimeout);
    await pool.ReleaseAsync(obj,
                            null,
                            cancellationTokenTimeout);

    Assert.That(x,
                Is.EqualTo(0));

    AssertThatAsync(pool.AcquireAsync(cancellationTokenTimeout),
                    Throws.InstanceOf<ApplicationException>());

    Assert.That(x,
                Is.EqualTo(1));

    AssertCanAcquire(pool);

    await pool.DisposeAsync();

    Assert.That(x,
                Is.EqualTo(2));
    cancellationTokenTimeout.ThrowIfCancellationRequested();
  }

  [Test]
  [CancelAfter(1000)]
  [AbortAfter(1500)]
  public async Task ValidateReleaseFailure([Values] bool?    asyncDispose,
                                           CancellationToken cancellationTokenTimeout)
  {
    var x = 0;
    var pool = new PoolRaw<object>(new PoolPolicy<object>().SetMaxNumberOfInstances(1)
                                                           .SetCreate(() => DisposeAction(asyncDispose,
                                                                                          () => ++x))
                                                           .SetValidateRelease(_ => ValueTaskExt.FromException<bool>(new ApplicationException())));

    var obj = await pool.AcquireAsync(cancellationTokenTimeout);

    AssertThatAsync(pool.ReleaseAsync(obj,
                                      null,
                                      cancellationTokenTimeout),
                    Throws.InstanceOf<ApplicationException>());

    Assert.That(x,
                Is.EqualTo(1));

    AssertCanAcquire(pool,
                     ignoreReleaseError: true);

    Assert.That(x,
                Is.EqualTo(2));

    await pool.DisposeAsync();

    Assert.That(x,
                Is.EqualTo(2));
    cancellationTokenTimeout.ThrowIfCancellationRequested();
  }

  [Test]
  [CancelAfter(1000)]
  [AbortAfter(1500)]
  public async Task CreateCancellation([Values] bool?    asyncDispose,
                                       CancellationToken cancellationTokenTimeout)
  {
    var       tcs = new TaskCompletionSource<ValueTuple>();
    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationTokenTimeout);

    var pool = new PoolRaw<ValueTuple>(new PoolPolicy<ValueTuple>().SetMaxNumberOfInstances(2)
                                                                   .SetCreate(async ct =>
                                                                              {
                                                                                tcs.SetResult(default);
                                                                                await ct.AsTask();

                                                                                return default;
                                                                              }));

    var task = pool.AcquireAsync(cts.Token);

    await Task.WhenAny(tcs.Task,
                       cancellationTokenTimeout.AsTask());

    // ReSharper disable once MethodHasAsyncOverload
    cts.Cancel();

    AssertThatAsync(task,
                    Throws.InstanceOf<TaskCanceledException>());
    cancellationTokenTimeout.ThrowIfCancellationRequested();
  }

  [Test]
  [CancelAfter(1000)]
  [AbortAfter(1500)]
  public async Task ValidateAcquireCancellation([Values] bool?    asyncDispose,
                                                CancellationToken cancellationTokenTimeout)
  {
    var       x    = 0;
    var       tcs1 = new TaskCompletionSource<ValueTuple>();
    using var cts  = CancellationTokenSource.CreateLinkedTokenSource(cancellationTokenTimeout);
    var pool = new PoolRaw<object>(new PoolPolicy<object>().SetMaxNumberOfInstances(1)
                                                           .SetCreate(() => DisposeAction(asyncDispose,
                                                                                          () => ++x))
                                                           .SetValidateAcquire(async (_,
                                                                                      _,
                                                                                      ct) =>
                                                                               {
                                                                                 tcs1.SetResult(default);
                                                                                 await ct.AsTask();

                                                                                 return true;
                                                                               }));

    var obj = await pool.AcquireAsync(cancellationTokenTimeout);
    await pool.ReleaseAsync(obj,
                            cancellationToken: cancellationTokenTimeout);

    var task = pool.AcquireAsync(cts.Token);
    await Task.WhenAny(tcs1.Task,
                       cancellationTokenTimeout.AsTask());

    // ReSharper disable once MethodHasAsyncOverload
    cts.Cancel();

    AssertThatAsync(task,
                    Throws.InstanceOf<OperationCanceledException>());

    Assert.That(x,
                Is.Zero);

    await pool.DisposeAsync();

    Assert.That(x,
                Is.EqualTo(1));
    cancellationTokenTimeout.ThrowIfCancellationRequested();
  }

  [Test]
  [CancelAfter(1000)]
  [AbortAfter(1500)]
  public async Task ValidateReleaseCancellation([Values] bool?    asyncDispose,
                                                CancellationToken cancellationTokenTimeout)
  {
    var       x   = 0;
    var       tcs = new TaskCompletionSource<ValueTuple>();
    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationTokenTimeout);
    var pool = new PoolRaw<object>(new PoolPolicy<object>().SetMaxNumberOfInstances(1)
                                                           .SetCreate(() => DisposeAction(asyncDispose,
                                                                                          () => ++x))
                                                           .SetValidateRelease(async (_,
                                                                                      _,
                                                                                      ct) =>
                                                                               {
                                                                                 tcs.SetResult(default);

                                                                                 await ct.AsTask();

                                                                                 return true;
                                                                               }));

    var obj = await pool.AcquireAsync(CancellationToken.None);

    var task = pool.ReleaseAsync(obj,
                                 cancellationToken: cts.Token);

    await Task.WhenAny(tcs.Task,
                       cancellationTokenTimeout.AsTask());

    // ReSharper disable once MethodHasAsyncOverload
    cts.Cancel();

    AssertThatAsync(task,
                    Throws.InstanceOf<OperationCanceledException>());
    Assert.That(x,
                Is.Zero);

    await pool.DisposeAsync();

    Assert.That(x,
                Is.EqualTo(1));
    cancellationTokenTimeout.ThrowIfCancellationRequested();
  }

  [Test]
  [CancelAfter(1000)]
  [AbortAfter(1500)]
  public async Task ValidateReleaseException([Values] bool?    asyncDispose,
                                             CancellationToken cancellationTokenTimeout)
  {
    var x = 0;
    var pool = new PoolRaw<object>(new PoolPolicy<object>().SetMaxNumberOfInstances(1)
                                                           .SetCreate(() => DisposeAction(asyncDispose,
                                                                                          () => ++x))
                                                           .SetValidateRelease((_,
                                                                                ex) => new ValueTask<bool>(ex is null)));

    var obj = await pool.AcquireAsync(cancellationTokenTimeout);

    // ReSharper disable once RedundantArgumentDefaultValue
    await pool.ReleaseAsync(obj,
                            null,
                            cancellationTokenTimeout);

    Assert.That(x,
                Is.Zero);

    Assert.That(await pool.AcquireAsync(cancellationTokenTimeout),
                Is.SameAs(obj));

    await pool.ReleaseAsync(obj,
                            new ApplicationException(),
                            cancellationTokenTimeout);

    Assert.That(x,
                Is.EqualTo(1));

    await pool.DisposeAsync();

    Assert.That(x,
                Is.EqualTo(1));
    cancellationTokenTimeout.ThrowIfCancellationRequested();
  }

  private static object DisposeAction(bool?  isAsync,
                                      Action f)
    => isAsync is null
         ? new SyncDisposeAction(f)
         : new AsyncDisposeAction(isAsync.Value,
                                  f);

  private record SyncDisposeAction(Action F) : IDisposable
  {
    public void Dispose()
      => F();
  }

  private record AsyncDisposeAction(bool   Async,
                                    Action F) : IAsyncDisposable
  {
    public async ValueTask DisposeAsync()
    {
      if (Async)
      {
        await Task.Yield();
      }

      F();
    }
  }

  private static void AssertThatAsync<T>(ValueTask<T>       task,
                                         IResolveConstraint expr)
    => Assert.That(() => task,
                   expr);

  private static void AssertThatAsync(ValueTask          task,
                                      IResolveConstraint expr)
    => Assert.That(() => task,
                   expr);

  private static async ValueTask<int> CanAcquire<T>(PoolRaw<T> pool,
                                                    int        max                = -1,
                                                    bool       ignoreReleaseError = false)
  {
    if (max == 0)
    {
      return 0;
    }

    var cts  = new CancellationTokenSource();
    var task = pool.AcquireAsync(cts.Token);
    if (task.TryGetSync(out var obj))
    {
      var result = await CanAcquire(pool,
                                    max < 0
                                      ? max
                                      : -1);
      // ReSharper disable once MethodHasAsyncOverload
      cts.Cancel();
      cts.Dispose();

      try
      {
        await pool.ReleaseAsync(obj,
                                null,
                                CancellationToken.None);
      }
      catch
      {
        if (!ignoreReleaseError)
        {
          throw;
        }
      }

      return result + 1;
    }

    // ReSharper disable once MethodHasAsyncOverload
    cts.Cancel();

    try
    {
      await pool.ReleaseAsync(await task,
                              null,
                              CancellationToken.None);
    }
    catch (OperationCanceledException)
    {
      // Empty on purpose
    }
    catch
    {
      if (!ignoreReleaseError)
      {
        throw;
      }
    }

    cts.Dispose();

    return 0;
  }

  private static void AssertCanAcquire<T>(PoolRaw<T> pool,
                                          int        n                  = 1,
                                          bool       exactly            = true,
                                          bool       ignoreReleaseError = false)
    => Assert.That(() => CanAcquire(pool,
                                    n + 1,
                                    ignoreReleaseError),
                   exactly
                     ? Is.EqualTo(n)
                     : Is.GreaterThanOrEqualTo(n),
                   "Wrong number of acquired objects from the pool");
}

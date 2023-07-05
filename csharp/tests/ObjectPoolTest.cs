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
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

namespace ArmoniK.Utils.Tests;

public class ObjectPoolTest
{
  [Test]
  [TestCase(false)]
  [TestCase(true)]
  public async Task ReuseObjectsFromPoolShouldSucceed(bool async)
  {
    var nbCreated = 0;
    await using var pool = new ObjectPool<int>(10,
                                               _ => new ValueTask<int>(nbCreated++),
                                               (i,
                                                _) => new ValueTask<bool>(i % 2 == 0));
    {
      var obj0 = await pool.GetAsync()
                           .ConfigureAwait(false);
      var obj1 = await pool.GetAsync()
                           .ConfigureAwait(false);

      Assert.That(nbCreated,
                  Is.EqualTo(2));
      Assert.That(obj0.Value,
                  Is.EqualTo(0));
      Assert.That(obj1.Value,
                  Is.EqualTo(1));

      if (async)
      {
        await obj0.DisposeAsync()
                  .ConfigureAwait(false);
        await obj1.DisposeAsync()
                  .ConfigureAwait(false);
      }
      else
      {
        // ReSharper disable once MethodHasAsyncOverload
        obj0.Dispose();
        // ReSharper disable once MethodHasAsyncOverload
        obj1.Dispose();
      }
    }

    {
      var obj0 = await pool.GetAsync()
                           .ConfigureAwait(false);
      var obj1 = await pool.GetAsync()
                           .ConfigureAwait(false);
      Assert.That(nbCreated,
                  Is.EqualTo(3));
      Assert.That(obj0.Value,
                  Is.EqualTo(0));
      Assert.That(obj1.Value,
                  Is.EqualTo(2));

      if (async)
      {
        await obj0.DisposeAsync()
                  .ConfigureAwait(false);
        await obj1.DisposeAsync()
                  .ConfigureAwait(false);
      }
      else
      {
        // ReSharper disable once MethodHasAsyncOverload
        obj0.Dispose();
        // ReSharper disable once MethodHasAsyncOverload
        obj1.Dispose();
      }
    }
  }

  [Test]
  [TestCase(false,
            false)]
  [TestCase(false,
            true)]
  [TestCase(true,
            false)]
  [TestCase(true,
            true)]
  public async Task PoolDisposeShouldSucceed(bool asyncDisposable,
                                             bool asyncDispose)
  {
    var nbDisposed = 0;
    var pool = new ObjectPool<object>(10,
                                      _ => new ValueTask<object>(asyncDisposable
                                                                   ? new AsyncDisposeAction(() => nbDisposed += 1)
                                                                   : new SyncDisposeAction(() => nbDisposed += 1)));

    await using (var obj = await pool.GetAsync()
                                     .ConfigureAwait(false))
    {
      _ = obj;
    }

    Assert.That(nbDisposed,
                Is.EqualTo(0));

    if (asyncDispose)
    {
      await pool.DisposeAsync()
                .ConfigureAwait(false);
    }
    else
    {
      // ReSharper disable once MethodHasAsyncOverload
      pool.Dispose();
    }

    Assert.That(nbDisposed,
                Is.EqualTo(1));
  }

  [Test]
  [TestCase(false,
            false)]
  [TestCase(false,
            true)]
  [TestCase(true,
            false)]
  [TestCase(true,
            true)]
  public async Task ReturnDisposeShouldSucceed(bool asyncDisposable,
                                               bool asyncDispose)
  {
    var nbDisposed = 0;
    var pool = new ObjectPool<object>(10,
                                      _ => new ValueTask<object>(asyncDisposable
                                                                   ? new AsyncDisposeAction(() => nbDisposed += 1)
                                                                   : new SyncDisposeAction(() => nbDisposed += 1)),
                                      (_,
                                       _) => new ValueTask<bool>(false));

    var obj = await pool.GetAsync()
                        .ConfigureAwait(false);

    Assert.That(nbDisposed,
                Is.EqualTo(0));

    if (asyncDispose)
    {
      await obj.DisposeAsync()
               .ConfigureAwait(false);
    }
    else
    {
      // ReSharper disable once MethodHasAsyncOverload
      obj.Dispose();
    }

    Assert.That(nbDisposed,
                Is.EqualTo(1));

    if (asyncDispose)
    {
      await pool.DisposeAsync()
                .ConfigureAwait(false);
    }
    else
    {
      // ReSharper disable once MethodHasAsyncOverload
      pool.Dispose();
    }

    Assert.That(nbDisposed,
                Is.EqualTo(1));
  }

  [Test]
  [TestCase(-1)]
  [TestCase(1)]
  [TestCase(4)]
  public async Task MaxLimitShouldSucceed(int max)
  {
    await using var pool = new ObjectPool<int>(max,
                                               _ => new ValueTask<int>(0));

    var n = max < 0
              ? 1000
              : max;

    for (var t = 0; t < 2; t += 1)
    {
      var guards = new List<ObjectPool<int>.Guard>();

      for (var i = 0; i < n; i += 1)
      {
        guards.Add(await pool.GetAsync()
                             .ConfigureAwait(false));
      }

      if (max > 0)
      {
        Assert.That(!await IsPoolAvailableAsync(pool)
                       .ConfigureAwait(false));
      }

      foreach (var guard in guards)
      {
        await guard.DisposeAsync()
                   .ConfigureAwait(false);
      }
    }
  }

  [Test]
  public async Task AcquireCancellation()
  {
    var       nbCreated = 0;
    using var cts0      = new CancellationTokenSource();
    using var cts1      = new CancellationTokenSource();
    using var cts2      = new CancellationTokenSource();
    await using var pool = new ObjectPool<int>(1,
                                               _ => new ValueTask<int>(nbCreated++));
    cts0.Cancel();
    Assert.That(() => pool.GetAsync(cts0.Token),
                Throws.InstanceOf<OperationCanceledException>());

    await using var obj = await pool.GetAsync(CancellationToken.None)
                                    .ConfigureAwait(false);
    var acquireTask = pool.GetAsync(cts2.Token);
    cts2.Cancel();
    Assert.That(() => acquireTask,
                Throws.InstanceOf<OperationCanceledException>());
  }


  [Test]
  public async Task CreateCancellation()
  {
    var       nbCreated = 0;
    using var cts       = new CancellationTokenSource();
    await using var pool = new ObjectPool<int>(1,
                                               async ct =>
                                               {
                                                 await Task.Delay(100,
                                                                  ct)
                                                           .ConfigureAwait(false);
                                                 return nbCreated++;
                                               });

    var acquireTask = pool.GetAsync(cts.Token);

    cts.Cancel();

    Assert.That(() => acquireTask,
                Throws.InstanceOf<OperationCanceledException>());

    var obj = await pool.GetAsync(CancellationToken.None)
                        .ConfigureAwait(false);

    Assert.That(obj.Value,
                Is.EqualTo(0));

    await obj.DisposeAsync()
             .ConfigureAwait(false);
  }

  [Test]
  public async Task CreateFailure()
  {
    var mustThrow = true;

    await using var pool = new ObjectPool<int>(1,
                                               _ =>
                                               {
                                                 if (!mustThrow)
                                                 {
                                                   return new ValueTask<int>(0);
                                                 }

                                                 mustThrow = false;
                                                 throw new ApplicationException("");
                                               });

    Assert.That(() => pool.GetAsync(),
                Throws.TypeOf<ApplicationException>());

    Assert.That(await IsPoolAvailableAsync(pool)
                  .ConfigureAwait(false));

    await using var obj = await pool.GetAsync()
                                    .ConfigureAwait(false);

    Assert.That(obj.Value,
                Is.EqualTo(0));
  }

  [Test]
  public async Task ReturnFailure()
  {
    var nbCreated = 0;
    var mustThrow = true;

    await using var pool = new ObjectPool<int>(1,
                                               _ => new ValueTask<int>(nbCreated++),
                                               (_,
                                                _) =>
                                               {
                                                 if (!mustThrow)
                                                 {
                                                   return new ValueTask<bool>(true);
                                                 }

                                                 mustThrow = false;
                                                 throw new ApplicationException("");
                                               });

    var obj = await pool.GetAsync()
                        .ConfigureAwait(false);

    Assert.That(obj.Value,
                Is.EqualTo(0));

    Assert.That(() => obj.DisposeAsync(),
                Throws.TypeOf<ApplicationException>());

    obj = await pool.GetAsync()
                    .ConfigureAwait(false);

    Assert.That(obj.Value,
                Is.EqualTo(1));

    await obj.DisposeAsync()
             .ConfigureAwait(false);
  }

  [Test]
  public async Task GuardFinalizer()
  {
    var nbCreated  = 0;
    var nbDisposed = 0;
    await using var pool = new ObjectPool<object>(1,
                                                  _ =>
                                                  {
                                                    nbCreated += 1;
                                                    return new ValueTask<object>(new AsyncDisposeAction(() => nbDisposed += 1));
                                                  },
                                                  (_,
                                                   _) => new ValueTask<bool>(false));

    var guardWeakReference = await CallWithOwnContext(async () =>
                                                      {
                                                        var guard = await pool.GetAsync();

                                                        GC.Collect();
                                                        GC.WaitForPendingFinalizers();

                                                        Assert.That(nbCreated,
                                                                    Is.EqualTo(1));
                                                        Assert.That(nbDisposed,
                                                                    Is.Zero);

                                                        return new WeakReference(guard);
                                                      });

    GC.Collect();
    GC.WaitForPendingFinalizers();

    Assert.That(guardWeakReference.IsAlive,
                Is.False);
    Assert.That(nbDisposed,
                Is.EqualTo(1));
  }

  [Test]
  public async Task PoolFinalizer()
  {
    var nbCreated  = 0;
    var nbDisposed = 0;

    var poolWeakReference = await CallWithOwnContext(async () =>
                                                     {
                                                       var pool = new ObjectPool<object>(1,
                                                                                         _ =>
                                                                                         {
                                                                                           nbCreated += 1;
                                                                                           return new ValueTask<object>(new Deferrer(() => nbDisposed += 1));
                                                                                         });

                                                       await using (await pool.GetAsync()
                                                                              .ConfigureAwait(false))
                                                       {
                                                       }

                                                       GC.Collect();
                                                       GC.WaitForPendingFinalizers();

                                                       Assert.That(nbCreated,
                                                                   Is.EqualTo(1));
                                                       Assert.That(nbDisposed,
                                                                   Is.Zero);

                                                       return new WeakReference(pool);
                                                     });


    GC.Collect();
    GC.WaitForPendingFinalizers();

    Assert.That(poolWeakReference.IsAlive,
                Is.False);

    // Why a 2nd collect is necessary here?
    GC.Collect();
    GC.WaitForPendingFinalizers();

    Assert.That(nbDisposed,
                Is.EqualTo(1));
  }

  [Test]
  public async Task Finalizer()
  {
    var nbCreated  = 0;
    var nbDisposed = 0;

    var (poolWeakReference, guardWeakReference) = await CallWithOwnContext(async () =>
                                                                           {
                                                                             var pool = new ObjectPool<object>(1,
                                                                                                               _ =>
                                                                                                               {
                                                                                                                 nbCreated += 1;
                                                                                                                 return new
                                                                                                                   ValueTask<object>(new Deferrer(() => nbDisposed +=
                                                                                                                                                          1));
                                                                                                               });

                                                                             var guard = await pool.GetAsync();

                                                                             GC.Collect();
                                                                             GC.WaitForPendingFinalizers();

                                                                             Assert.That(nbCreated,
                                                                                         Is.EqualTo(1));
                                                                             Assert.That(nbDisposed,
                                                                                         Is.Zero);

                                                                             return (new WeakReference(pool), new WeakReference(guard));
                                                                           });


    GC.Collect();
    GC.WaitForPendingFinalizers();

    Assert.That(poolWeakReference.IsAlive,
                Is.False);
    Assert.That(guardWeakReference.IsAlive,
                Is.False);

    Assert.That(nbDisposed,
                Is.EqualTo(1));
  }


  [Test]
  [TestCase(false,
            false)]
  [TestCase(false,
            true)]
  [TestCase(true,
            false)]
  [TestCase(true,
            true)]
  public async Task PoolDisposeThrow(bool asyncDisposable,
                                     bool asyncDispose)
  {
    var nbDisposed = 0;

    void Dispose()
    {
      switch (++nbDisposed % 4)
      {
        case 1:
          throw new ApplicationException("first");
        case 3:
          throw new ApplicationException("second");
      }
    }

    var pool = new ObjectPool<object>(10,
                                      _ => new ValueTask<object>(asyncDisposable
                                                                   ? new AsyncDisposeAction(Dispose)
                                                                   : new SyncDisposeAction(Dispose)));


    await using (await pool.GetAsync()
                           .ConfigureAwait(false))
    await using (await pool.GetAsync()
                           .ConfigureAwait(false))
    await using (await pool.GetAsync()
                           .ConfigureAwait(false))
    await using (await pool.GetAsync()
                           .ConfigureAwait(false))
    {
    }

    Assert.That(nbDisposed,
                Is.EqualTo(0));

    AggregateException ex;

    if (asyncDispose)
    {
      ex = Assert.ThrowsAsync<AggregateException>(() => pool.DisposeAsync()
                                                            .AsTask())!;
    }
    else
    {
      ex = Assert.Throws<AggregateException>(() => pool.Dispose())!;
    }

    Assert.Multiple(() =>
                    {
                      Assert.That(ex.InnerExceptions,
                                  Has.Count.EqualTo(2));
                      Assert.That(ex.InnerExceptions,
                                  Has.All.TypeOf<ApplicationException>());
                      Assert.That(ex.InnerExceptions[0],
                                  Has.Message.EqualTo("first"));
                      Assert.That(ex.InnerExceptions[1],
                                  Has.Message.EqualTo("second"));

                      Assert.That(nbDisposed,
                                  Is.EqualTo(4));
                    });
  }

  [Test]
  [TestCase(false,
            false)]
  [TestCase(false,
            true)]
  [TestCase(true,
            false)]
  [TestCase(true,
            true)]
  public async Task ReturnDisposeThrow(bool asyncDisposable,
                                       bool asyncDispose)
  {
    var pool = new ObjectPool<object>(10,
                                      _ => new ValueTask<object>(asyncDisposable
                                                                   ? new AsyncDisposeAction(() => throw new ApplicationException())
                                                                   : new SyncDisposeAction(() => throw new ApplicationException())),
                                      (_,
                                       _) => new ValueTask<bool>(false));

    var obj = await pool.GetAsync()
                        .ConfigureAwait(false);

    if (asyncDispose)
    {
      Assert.That(() => obj.DisposeAsync(),
                  Throws.TypeOf<ApplicationException>());
    }
    else
    {
      Assert.That(() => obj.Dispose(),
                  Throws.TypeOf<ApplicationException>());
    }

    if (asyncDispose)
    {
      Assert.That(() => pool.DisposeAsync(),
                  Throws.Nothing);
    }
    else
    {
      Assert.That(() => pool.Dispose(),
                  Throws.Nothing);
    }
  }

  private static ValueTask<T> CallWithOwnContext<T>(Func<ValueTask<T>> f)
    => f();

  private static async ValueTask<bool> IsPoolAvailableAsync<T>(ObjectPool<T> pool)
  {
    var cts     = new CancellationTokenSource();
    var getTask = pool.GetAsync(cts.Token);
    await Task.Delay(1,
                     CancellationToken.None)
              .ConfigureAwait(false);
    cts.Cancel();
    try
    {
      await using var guard = await getTask.ConfigureAwait(false);
      return true;
    }
    catch (OperationCanceledException)
    {
      return false;
    }
  }

  private record SyncDisposeAction(Action F) : IDisposable
  {
    public void Dispose()
      => F();
  }

  private record AsyncDisposeAction(Action F) : IAsyncDisposable
  {
    public async ValueTask DisposeAsync()
    {
      await Task.Yield();
      F();
    }
  }
}

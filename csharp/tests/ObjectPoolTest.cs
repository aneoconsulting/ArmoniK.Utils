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

using NUnit.Framework;

namespace ArmoniK.Utils.Tests;

// ReSharper disable MethodHasAsyncOverload
// ReSharper disable UseAwaitUsing
public class ObjectPoolTest
{
  public enum Project
  {
    No,
    Sync,
    Async,
  }

  public enum UseMethod
  {
    Get,
    WithFunc,
    WithAction,
  }

  [Test]
  public async Task ReuseObjectsFromPoolShouldSucceed([Values] bool      asyncFactory,
                                                      [Values] UseMethod useMethod,
                                                      [Values] bool      asyncUse,
                                                      [Values] bool      asyncContext,
                                                      [Values] Project   project,
                                                      [Values] Project   projectGet)
  {
    var nbCreated = 0;
    await using var pool = Projected(project,
                                     asyncFactory
                                       ? new ObjectPool<int>(_ => new ValueTask<int>(nbCreated++),
                                                             (i,
                                                              _) => new ValueTask<bool>(i % 2 == 0))
                                       : new ObjectPool<int>(() => nbCreated++,
                                                             i => i % 2 == 0));

    // ReSharper disable AccessToDisposedClosure
    async Task CheckAcquire(int created,
                            int ref0,
                            int ref1)
    {
      var val0 = -1;
      var val1 = -1;
      switch (useMethod, asyncContext, asyncUse)
      {
        case (UseMethod.Get, _, _):
          var obj0 = await Get(pool,
                               asyncUse,
                               projectGet)
                       .ConfigureAwait(false);
          var obj1 = await Get(pool,
                               asyncUse,
                               projectGet)
                       .ConfigureAwait(false);

          val0 = obj0;
          val1 = obj1;

          if (asyncContext)
          {
            await obj0.DisposeAsync()
                      .ConfigureAwait(false);
            await obj1.DisposeAsync()
                      .ConfigureAwait(false);
          }
          else
          {
            obj0.Dispose();
            obj1.Dispose();
          }

          break;
        case (UseMethod.WithFunc, false, _):
          (val0, val1) = pool.WithInstance(x => pool.WithInstance(y => (x, y)));
          break;
        case (UseMethod.WithFunc, true, false):
          (val0, val1) = await pool.WithInstanceAsync(x => pool.WithInstanceAsync(y => (x, y))
                                                               .AsTask()
                                                               .Result)
                                   .ConfigureAwait(false);
          break;
        case (UseMethod.WithFunc, true, true):
          (val0, val1) = await pool.WithInstanceAsync(x => pool.WithInstanceAsync(y => new ValueTask<(int, int)>((x, y))))
                                   .ConfigureAwait(false);
          break;
        case (UseMethod.WithAction, false, _):
          pool.WithInstance(x => pool.WithInstance(y =>
                                                   {
                                                     (val0, val1) = (x, y);
                                                   }));
          break;
        case (UseMethod.WithAction, true, false):
          await pool.WithInstanceAsync(x => pool.WithInstanceAsync(y =>
                                                                   {
                                                                     (val0, val1) = (x, y);
                                                                   })
                                                .AsTask()
                                                .Wait())
                    .ConfigureAwait(false);
          break;
        case (UseMethod.WithAction, true, true):
          await pool.WithInstanceAsync(x => pool.WithInstanceAsync(y =>
                                                                   {
                                                                     (val0, val1) = (x, y);
                                                                   }))
                    .ConfigureAwait(false);
          break;
      }
      // ReSharper restore AccessToDisposedClosure


      Assert.That(nbCreated,
                  Is.EqualTo(created));
      Assert.That(val0,
                  Is.EqualTo(ref0));
      Assert.That(val1,
                  Is.EqualTo(ref1));
    }

    await CheckAcquire(2,
                       0,
                       1)
      .ConfigureAwait(false);
    await CheckAcquire(3,
                       0,
                       2)
      .ConfigureAwait(false);
  }

  [Test]
  public async Task PoolDisposeShouldSucceed([Values] bool    asyncDisposable,
                                             [Values] bool    asyncDispose,
                                             [Values] bool    asyncFactory,
                                             [Values] Project project)
  {
    var nbDisposed = 0;

    object Factory()
      => asyncDisposable
           ? new AsyncDisposeAction(() => nbDisposed += 1)
           : new SyncDisposeAction(() => nbDisposed += 1);

    var pool = Projected(project,
                         asyncFactory
                           ? new ObjectPool<object>(_ => new ValueTask<object>(Factory()))
                           : new ObjectPool<object>(Factory));

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
      pool.Dispose();
    }

    Assert.That(nbDisposed,
                Is.EqualTo(1));
  }

  [Test]
  public async Task ReturnDisposeShouldSucceed([Values] bool    asyncDisposable,
                                               [Values] bool    asyncDispose,
                                               [Values] bool    asyncFactory,
                                               [Values] Project project,
                                               [Values] Project projectGet)
  {
    var nbDisposed = 0;

    object Factory()
      => asyncDisposable
           ? new AsyncDisposeAction(() => nbDisposed += 1)
           : new SyncDisposeAction(() => nbDisposed += 1);

    var pool = Projected(project,
                         asyncFactory
                           ? new ObjectPool<object>(_ => new ValueTask<object>(Factory()),
                                                    (_,
                                                     _) => new ValueTask<bool>(false))
                           : new ObjectPool<object>(Factory,
                                                    _ => false));

    var obj = await Get(pool,
                        true,
                        projectGet)
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
      pool.Dispose();
    }

    Assert.That(nbDisposed,
                Is.EqualTo(1));
  }

  [Test]
  [SuppressMessage("ReSharper",
                   "AccessToModifiedClosure")]
  public async Task DelayedReturnDisposeShouldSucceed([Values] bool    asyncDisposable,
                                                      [Values] bool    asyncDispose,
                                                      [Values] bool    asyncFactory,
                                                      [Values] Project project,
                                                      [Values] Project projectGet)
  {
    var nbDisposed = 0;

    object Factory()
      => asyncDisposable
           ? new AsyncDisposeAction(() => nbDisposed += 1)
           : new SyncDisposeAction(() => nbDisposed += 1);

    var poolObjectsAreValid = true;

    var pool = Projected(project,
                         asyncFactory
                           ? new ObjectPool<object>(_ => new ValueTask<object>(Factory()),
                                                    (_,
                                                     _) => new ValueTask<bool>(poolObjectsAreValid))
                           : new ObjectPool<object>(Factory,
                                                    _ => poolObjectsAreValid));

    {
      var obj1 = await Get(pool,
                           true,
                           projectGet)
                   .ConfigureAwait(false);
      var obj2 = await Get(pool,
                           true,
                           projectGet)
                   .ConfigureAwait(false);

      Assert.That(nbDisposed,
                  Is.EqualTo(0));

      if (asyncDispose)
      {
        await obj1.DisposeAsync()
                  .ConfigureAwait(false);
        await obj2.DisposeAsync()
                  .ConfigureAwait(false);
      }
      else
      {
        obj1.Dispose();
        obj2.Dispose();
      }
    }

    Assert.That(nbDisposed,
                Is.EqualTo(0));

    poolObjectsAreValid = false;

    {
      var obj = await Get(pool,
                          true,
                          projectGet)
                  .ConfigureAwait(false);

      Assert.That(nbDisposed,
                  Is.EqualTo(2));

      if (asyncDispose)
      {
        await obj.DisposeAsync()
                 .ConfigureAwait(false);
      }
      else
      {
        obj.Dispose();
      }
    }

    Assert.That(nbDisposed,
                Is.EqualTo(3));

    if (asyncDispose)
    {
      await pool.DisposeAsync()
                .ConfigureAwait(false);
    }
    else
    {
      pool.Dispose();
    }

    Assert.That(nbDisposed,
                Is.EqualTo(3));
  }

  [Test]
  public async Task MaxLimitShouldSucceed([Values(null,
                                                  -1,
                                                  1,
                                                  4)]
                                          int? max,
                                          [Values] bool      asyncFactory,
                                          [Values] UseMethod useMethod,
                                          [Values] bool      asyncUse,
                                          [Values] bool      asyncContext,
                                          [Values] Project   project,
                                          [Values] Project   projectGet)
  {
    // ReSharper disable AccessToDisposedClosure
    // ReSharper disable ConvertTypeCheckPatternToNullCheck
    await using var pool = Projected(project,
                                     (max, asyncFactory) switch
                                     {
                                       (null, false) => new ObjectPool<int>(_ => new ValueTask<int>(0)),
                                       (null, true)  => new ObjectPool<int>(() => 0),
                                       (int m, false) => new ObjectPool<int>(m,
                                                                             _ => new ValueTask<int>(0)),
                                       (int m, true) => new ObjectPool<int>(m,
                                                                            () => 0),
                                     });
    // ReSharper restore ConvertTypeCheckPatternToNullCheck

    var n = (max ?? -1) < 0
              ? 100
              : max;


    Func<Func<ValueTask>, ValueTask> recurse = (useMethod, asyncContext, asyncUse) switch
                                               {
                                                 (UseMethod.Get, false, _) => async f =>
                                                                              {
                                                                                using var guard = await Get(pool,
                                                                                                            asyncUse,
                                                                                                            projectGet)
                                                                                                    .ConfigureAwait(false);
                                                                                await f()
                                                                                  .ConfigureAwait(false);
                                                                              },
                                                 (UseMethod.Get, true, _) => async f =>
                                                                             {
                                                                               await using var guard = await Get(pool,
                                                                                                                 asyncUse,
                                                                                                                 projectGet)
                                                                                                         .ConfigureAwait(false);
                                                                               await f()
                                                                                 .ConfigureAwait(false);
                                                                             },
                                                 (UseMethod.WithFunc, false, _) => f =>
                                                                                   {
                                                                                     _ = pool.WithInstance(x =>
                                                                                                           {
                                                                                                             f()
                                                                                                               .AsTask()
                                                                                                               .Wait();
                                                                                                             return x;
                                                                                                           });
                                                                                     return new ValueTask();
                                                                                   },
                                                 (UseMethod.WithFunc, true, false) => async f =>
                                                                                      {
                                                                                        _ = await pool.WithInstanceAsync(x =>
                                                                                                                         {
                                                                                                                           f()
                                                                                                                             .AsTask()
                                                                                                                             .Wait();
                                                                                                                           return x;
                                                                                                                         })
                                                                                                      .ConfigureAwait(false);
                                                                                      },
                                                 (UseMethod.WithFunc, true, true) => async f =>
                                                                                     {
                                                                                       _ = await pool.WithInstanceAsync(async x =>
                                                                                                                        {
                                                                                                                          await f()
                                                                                                                            .ConfigureAwait(false);
                                                                                                                          return x;
                                                                                                                        })
                                                                                                     .ConfigureAwait(false);
                                                                                     },
                                                 (UseMethod.WithAction, false, _) => f =>
                                                                                     {
                                                                                       pool.WithInstance(_ => f()
                                                                                                              .AsTask()
                                                                                                              .Wait());
                                                                                       return new ValueTask();
                                                                                     },
                                                 (UseMethod.WithAction, true, false) => async f =>
                                                                                        {
                                                                                          await pool.WithInstanceAsync(_ => f()
                                                                                                                            .AsTask()
                                                                                                                            .Wait())
                                                                                                    .ConfigureAwait(false);
                                                                                        },
                                                 (UseMethod.WithAction, true, true) => async f =>
                                                                                       {
                                                                                         await pool.WithInstanceAsync(async _ => await f()
                                                                                                                                   .ConfigureAwait(false))
                                                                                                   .ConfigureAwait(false);
                                                                                       },
                                                 _ => throw new ArgumentOutOfRangeException(nameof(useMethod)),
                                               };


    for (var t = 0; t < 4; t += 1)
    {
      var called = false;
      var i      = 0;

      async ValueTask RecurseBody()
      {
        // ReSharper disable once AccessToModifiedClosure
        if (i++ < n)
        {
          await recurse(RecurseBody)
            .ConfigureAwait(false);
        }
        else
        {
          called = true;

          if (max > 0)
          {
            Assert.That(!await IsPoolAvailableAsync(pool)
                           .ConfigureAwait(false));
          }
        }
      }

      await RecurseBody()
        .ConfigureAwait(false);

      Assert.That(called);
    }
    // ReSharper restore AccessToDisposedClosure
  }

  [Test]
  public async Task AcquireCancellation([Values] bool    asyncFactory,
                                        [Values] Project project)
  {
    var       nbCreated = 0;
    using var cts0      = new CancellationTokenSource();
    using var cts1      = new CancellationTokenSource();
    using var cts2      = new CancellationTokenSource();
    await using var pool = Projected(project,
                                     asyncFactory
                                       ? new ObjectPool<int>(1,
                                                             _ => new ValueTask<int>(nbCreated++))
                                       : new ObjectPool<int>(1,
                                                             () => nbCreated++));
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
  public async Task CreateCancellation([Values] Project project)
  {
    var       nbCreated = 0;
    using var cts       = new CancellationTokenSource();
    await using var pool = Projected(project,
                                     new ObjectPool<int>(1,
                                                         async ct =>
                                                         {
                                                           await Task.Delay(100,
                                                                            ct)
                                                                     .ConfigureAwait(false);
                                                           return nbCreated++;
                                                         }));

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
  public async Task CreateFailure([Values] Project project)
  {
    var mustThrow = true;

    await using var pool = Projected(project,
                                     new ObjectPool<int>(1,
                                                         _ =>
                                                         {
                                                           if (!mustThrow)
                                                           {
                                                             return new ValueTask<int>(0);
                                                           }

                                                           mustThrow = false;
                                                           throw new ApplicationException("");
                                                         }));

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
  public async Task ReturnFailure([Values] Project project)
  {
    var nbCreated = 0;
    var mustThrow = true;

    await using var pool = Projected(project,
                                     new ObjectPool<int>(1,
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
                                                         }));

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
  public async Task GuardFinalizer([Values] Project project,
                                   [Values] Project projectGet)
  {
    var nbCreated  = 0;
    var nbDisposed = 0;
    await using var pool = Projected(project,
                                     new ObjectPool<object>(1,
                                                            _ =>
                                                            {
                                                              nbCreated += 1;
                                                              return new ValueTask<object>(new AsyncDisposeAction(() => nbDisposed += 1));
                                                            },
                                                            (_,
                                                             _) => new ValueTask<bool>(false)));

    var guardWeakReference = await CallWithOwnContext(async () =>
                                                      {
                                                        var guard = await Get(pool,
                                                                              true,
                                                                              projectGet);

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
  public async Task PoolFinalizer([Values] Project project)
  {
    var nbCreated  = 0;
    var nbDisposed = 0;

    var poolWeakReference = await CallWithOwnContext(async () =>
                                                     {
                                                       var pool = Projected(project,
                                                                            new ObjectPool<object>(1,
                                                                                                   _ =>
                                                                                                   {
                                                                                                     nbCreated += 1;
                                                                                                     return new ValueTask<object>(new Deferrer(() => nbDisposed += 1));
                                                                                                   }));

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
  public async Task Finalizer([Values] Project project,
                              [Values] Project projectGet)
  {
    var nbCreated  = 0;
    var nbDisposed = 0;

    var (poolWeakReference, guardWeakReference) = await CallWithOwnContext(async () =>
                                                                           {
                                                                             var pool = Projected(project,
                                                                                                  new ObjectPool<object>(1,
                                                                                                                         _ =>
                                                                                                                         {
                                                                                                                           nbCreated += 1;
                                                                                                                           return new
                                                                                                                             ValueTask<
                                                                                                                               object>(new Deferrer(() => nbDisposed +=
                                                                                                                                                            1));
                                                                                                                         }));

                                                                             var guard = await Get(pool,
                                                                                                   true,
                                                                                                   projectGet)
                                                                                           .ConfigureAwait(false);

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
  public async Task PoolDisposeWithGuardAlive([Values] bool    asyncDispose,
                                              [Values] Project project,
                                              [Values] Project projectGet)
  {
    var pool = Projected(project,
                         new ObjectPool<ValueTuple>(10,
                                                    _ => new ValueTask<ValueTuple>(new ValueTuple())));

    var guard = await Get(pool,
                          true,
                          projectGet)
                  .ConfigureAwait(false);

    Assert.Multiple(() =>
                    {
                      if (asyncDispose)
                      {
                        Assert.That(() => pool.DisposeAsync(),
                                    Throws.Nothing);
                        Assert.That(() => guard.DisposeAsync(),
                                    Throws.InstanceOf<ObjectDisposedException>());
                      }
                      else
                      {
                        Assert.That(() => pool.Dispose(),
                                    Throws.Nothing);
                        Assert.That(() => guard.Dispose(),
                                    Throws.InstanceOf<ObjectDisposedException>());
                      }
                    });
  }


  [Test]
  public async Task PoolDisposeThrow([Values] bool    asyncDisposable,
                                     [Values] bool    asyncDispose,
                                     [Values] Project project)
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

    var pool = Projected(project,
                         new ObjectPool<object>(10,
                                                _ => new ValueTask<object>(asyncDisposable
                                                                             ? new AsyncDisposeAction(Dispose)
                                                                             : new SyncDisposeAction(Dispose))));


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
  public async Task ReturnDisposeThrow([Values] bool    asyncDisposable,
                                       [Values] bool    asyncDispose,
                                       [Values] Project project,
                                       [Values] Project projectGet)
  {
    var pool = Projected(project,
                         new ObjectPool<object>(_ => new ValueTask<object>(asyncDisposable
                                                                             ? new AsyncDisposeAction(() => throw new ApplicationException())
                                                                             : new SyncDisposeAction(() => throw new ApplicationException())),
                                                (_,
                                                 _) => new ValueTask<bool>(false)));

    var obj = await Get(pool,
                        true,
                        projectGet)
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

  private static ObjectPool<T> Projected<T>(Project       project,
                                            ObjectPool<T> pool,
                                            bool          disposeBasePool = true)
  {
    var newPool = project switch
                  {
                    Project.No   => pool,
                    Project.Sync => pool.Project(x => x),
                    Project.Async => pool.Project((x,
                                                   _) => new ValueTask<T>(x)),
                    _ => throw new ArgumentException("Invalid project",
                                                     nameof(project)),
                  };

    if (disposeBasePool && !ReferenceEquals(pool,
                                            newPool))
    {
      pool.Dispose();
    }

    return newPool;
  }

  private static ValueTask<ObjectPool<T>.Guard> Get<T>(ObjectPool<T> pool,
                                                       bool          async,
                                                       Project       project)
    => (async, project) switch
       {
         (false, Project.No)    => new ValueTask<ObjectPool<T>.Guard>(pool.Get()),
         (false, Project.Sync)  => new ValueTask<ObjectPool<T>.Guard>(pool.Get(x => x)),
         (false, Project.Async) => new ValueTask<ObjectPool<T>.Guard>(pool.Get(x => x)),
         (true, Project.No)     => pool.GetAsync(),
         (true, Project.Sync)   => pool.GetAsync(x => x),
         (true, Project.Async)  => pool.GetAsync(x => new ValueTask<T>(x)),
         _ => throw new ArgumentException("Invalid",
                                          nameof(project)),
       };

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

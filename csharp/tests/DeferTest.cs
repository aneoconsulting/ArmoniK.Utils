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

using NUnit.Framework;

namespace ArmoniK.Utils.Tests;

public class DeferTest
{
  ////////////////////////////
  // Synchronous Disposable //
  ////////////////////////////
  [Test]
  public void DeferEmptyShouldWork()
  {
    using var defer = Deferrer.Empty;
  }

  [Test]
  public void DeferDefaultShouldWork()
  {
    using var defer = new Deferrer();
  }

  [Test]
  public void DeferShouldWork([Values] bool async)
  {
    var i = 1;
    using (DisposableCreate(async,
                            0,
                            () => i += 1))
    {
      Assert.That(i,
                  Is.EqualTo(1));
    }

    Assert.That(i,
                Is.EqualTo(2));
  }


  [Test]
  public void RedundantDeferShouldWork([Values] bool async)
  {
    var i = 1;

    var defer = DisposableCreate(async,
                                 0,
                                 () => i += 1);

    Assert.That(i,
                Is.EqualTo(1));

    defer.Dispose();

    Assert.That(i,
                Is.EqualTo(2));

    defer.Dispose();

    Assert.That(i,
                Is.EqualTo(2));
  }

  [Test]
  public void DeferResetShouldWork([Values] bool? firstAsync,
                                   [Values] bool? secondAsync)
  {
    var first  = false;
    var second = false;

    using (var disposable = DeferrerCreate(firstAsync,
                                           0,
                                           () => first = true))
    {
      switch (secondAsync)
      {
        case null:
          disposable.Reset();
          break;
        case false:
          disposable.Reset(() => second = true);
          break;
        case true:
          disposable.Reset(async () =>
                           {
                             await Task.Yield();
                             second = true;
                           });
          break;
      }
    }

    // Force collection to ensure that previous action is not called
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
    GC.WaitForPendingFinalizers();

    Assert.That(first,
                Is.False);
    if (secondAsync is null)
    {
      Assert.That(second,
                  Is.False);
    }
    else
    {
      Assert.That(second,
                  Is.True);
    }
  }

  [Test]
  public async Task DeferShouldBeRaceConditionFree([Values] bool async)
  {
    var i = 1;

    var defer = DisposableCreate(async,
                                 100,
                                 () => Interlocked.Increment(ref i));

    var task1 = Task.Run(() => defer.Dispose());
    var task2 = Task.Run(() => defer.Dispose());

    await task1;
    await task2;

    Assert.That(i,
                Is.EqualTo(2));
  }

  [Test]
  public void RedundantCopyDeferShouldWork([Values] bool async)
  {
    var i = 1;

    {
      using var defer1 = DisposableCreate(async,
                                          100,
                                          () => i += 1);
      using var defer2 = defer1;

      Assert.That(i,
                  Is.EqualTo(1));
    }

    Assert.That(i,
                Is.EqualTo(2));
  }

  private static WeakReference WeakRefDisposable(Func<IDisposable> f)
    => new(f());

  [Test]
  public void DeferShouldWorkWhenCollected([Values] bool async)
  {
    var i = 1;

    IDisposable reference;

    var weakRef = WeakRefDisposable(() =>
                                    {
                                      reference = DisposableCreate(async,
                                                                   0,
                                                                   () => i += 1);
                                      return reference;
                                    });

    GC.Collect();
    GC.WaitForPendingFinalizers();

    Assert.That(weakRef.IsAlive,
                Is.True);
    Assert.That(i,
                Is.EqualTo(1));

    reference = Deferrer.Empty;

    GC.Collect();
    GC.WaitForPendingFinalizers();

    Assert.That(weakRef.IsAlive,
                Is.False);

    Assert.That(i,
                Is.EqualTo(2));
  }

  [Test]
  public void WrappedDeferShouldWork([Values] bool async)
  {
    var i = 1;
    using (new DisposableWrapper(DisposableCreate(async,
                                                  0,
                                                  () => i += 1)))
    {
      Assert.That(i,
                  Is.EqualTo(1));
    }

    Assert.That(i,
                Is.EqualTo(2));
  }

  /////////////////////////////
  // Asynchronous Disposable //
  /////////////////////////////
  [Test]
  public async Task AsyncDeferEmptyShouldWork()
  {
    await using var defer = Deferrer.Empty;
  }

  [Test]
  public async Task AsyncDeferDefaultShouldWork()
  {
    await using var defer = new Deferrer();
  }

  [Test]
  public async Task AsyncDeferShouldWork([Values] bool async)
  {
    var i = 1;
    await using (AsyncDisposableCreate(async,
                                       0,
                                       () => i += 1))
    {
      Assert.That(i,
                  Is.EqualTo(1));
    }

    Assert.That(i,
                Is.EqualTo(2));
  }


  [Test]
  public async Task RedundantAsyncDeferShouldWork([Values] bool async)
  {
    var i = 1;

    var defer = AsyncDisposableCreate(async,
                                      0,
                                      () => i += 1);

    Assert.That(i,
                Is.EqualTo(1));

    await defer.DisposeAsync()
               .ConfigureAwait(false);

    Assert.That(i,
                Is.EqualTo(2));

    await defer.DisposeAsync()
               .ConfigureAwait(false);

    Assert.That(i,
                Is.EqualTo(2));
  }

  [Test]
  public async Task AsyncDeferResetShouldWork([Values] bool? firstAsync,
                                              [Values] bool? secondAsync)
  {
    var first  = false;
    var second = false;

    await using (var disposable = DeferrerCreate(firstAsync,
                                                 0,
                                                 () => first = true))
    {
      switch (secondAsync)
      {
        case null:
          disposable.Reset();
          break;
        case false:
          disposable.Reset(() => second = true);
          break;
        case true:
          disposable.Reset(async () =>
                           {
                             await Task.Yield();
                             second = true;
                           });
          break;
      }
    }

    // Force collection to ensure that previous action is not called
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
    GC.WaitForPendingFinalizers();

    Assert.That(first,
                Is.False);
    if (secondAsync is null)
    {
      Assert.That(second,
                  Is.False);
    }
    else
    {
      Assert.That(second,
                  Is.True);
    }
  }

  [Test]
  public async Task AsyncDeferShouldBeRaceConditionFree([Values] bool async)
  {
    var i = 1;

    var defer = AsyncDisposableCreate(async,
                                      100,
                                      () => Interlocked.Increment(ref i));

    var task1 = Task.Run(async () => await defer.DisposeAsync()
                                                .ConfigureAwait(false));
    var task2 = Task.Run(async () => await defer.DisposeAsync()
                                                .ConfigureAwait(false));

    await task1;
    await task2;

    Assert.That(i,
                Is.EqualTo(2));
  }

  [Test]
  public async Task RedundantCopyAsyncDeferShouldWork([Values] bool async)
  {
    var i = 1;

    {
      await using var defer1 = AsyncDisposableCreate(async,
                                                     100,
                                                     () => i += 1);
      await using var defer2 = defer1;

      Assert.That(i,
                  Is.EqualTo(1));
    }

    Assert.That(i,
                Is.EqualTo(2));
  }

  private static WeakReference WeakRefAsyncDisposable(Func<IAsyncDisposable> f)
    => new(f());

  [Test]
  public void AsyncDeferShouldWorkWhenCollected([Values] bool async)
  {
    var i = 1;

    IAsyncDisposable reference;

    var weakRef = WeakRefAsyncDisposable(() =>
                                         {
                                           reference = AsyncDisposableCreate(async,
                                                                             0,
                                                                             () => i += 1);
                                           return reference;
                                         });

    GC.Collect();
    GC.WaitForPendingFinalizers();

    Assert.That(weakRef.IsAlive,
                Is.True);
    Assert.That(i,
                Is.EqualTo(1));

    reference = Deferrer.Empty;

    GC.Collect();
    GC.WaitForPendingFinalizers();

    Assert.That(weakRef.IsAlive,
                Is.False);

    Assert.That(i,
                Is.EqualTo(2));
  }

  [Test]
  public async Task WrappedAsyncDeferShouldWork([Values] bool async)
  {
    var i = 1;
    await using (new AsyncDisposableWrapper(AsyncDisposableCreate(async,
                                                                  0,
                                                                  () => i += 1)))
    {
      Assert.That(i,
                  Is.EqualTo(1));
    }

    Assert.That(i,
                Is.EqualTo(2));
  }

  private static Deferrer DeferrerCreate(bool?  async,
                                         int    delay,
                                         Action f)
  {
    switch (async)
    {
      case null:
        return new Deferrer();
      case false:
        return new Deferrer(() =>
                            {
                              if (delay > 0)
                              {
                                Thread.Sleep(delay);
                              }
                              else
                              {
                                Thread.Yield();
                              }

                              f();
                            });
      case true:
        return new Deferrer(async () =>
                            {
                              if (delay > 0)
                              {
                                await Task.Delay(delay);
                              }
                              else
                              {
                                await Task.Yield();
                              }

                              f();
                            });
    }
  }

  private static IDisposable DisposableCreate(bool?  async,
                                              int    delay,
                                              Action f)
    => DeferrerCreate(async,
                      delay,
                      f);

  private static IAsyncDisposable AsyncDisposableCreate(bool?  async,
                                                        int    delay,
                                                        Action f)
    => DeferrerCreate(async,
                      delay,
                      f);

  ///////////
  // Utils //
  ///////////
  private record DisposableWrapper(IDisposable Disposable) : IDisposable
  {
    public void Dispose()
      => Disposable.Dispose();
  }

  private record AsyncDisposableWrapper(IAsyncDisposable AsyncDisposable) : IAsyncDisposable
  {
    public ValueTask DisposeAsync()
      => AsyncDisposable.DisposeAsync();
  }
}

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
  public enum DeferrerKind
  {
    SyncFunc,
    AsyncFunc,
    SyncDisposable,
    AsyncDisposable,
  }

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
  public void DeferShouldWork([Values] DeferrerKind kind)
  {
    var i = 1;
    using (DisposableCreate(kind,
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
  public void RedundantDeferShouldWork([Values] DeferrerKind kind)
  {
    var i = 1;

    var defer = DisposableCreate(kind,
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
  public void DeferResetShouldWork([Values] DeferrerKind? firstKind,
                                   [Values] DeferrerKind? secondKind)
  {
    var first  = false;
    var second = false;

    using (var disposable = DeferrerCreate(firstKind,
                                           0,
                                           () => first = true))
    {
      DeferrerReset(disposable,
                    secondKind,
                    0,
                    () => second = true);
    }

    // Force collection to ensure that previous action is not called
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
    GC.WaitForPendingFinalizers();

    Assert.That(first,
                Is.False);
    if (secondKind is null)
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
  public async Task DeferShouldBeRaceConditionFree([Values] DeferrerKind kind)
  {
    var i = 1;

    var defer = DisposableCreate(kind,
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
  public void RedundantCopyDeferShouldWork([Values] DeferrerKind kind)
  {
    var i = 1;

    {
      using var defer1 = DisposableCreate(kind,
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
  public void DeferShouldWorkWhenCollected([Values] DeferrerKind kind)
  {
    var i = 1;

    IDisposable reference;

    var weakRef = WeakRefDisposable(() =>
                                    {
                                      reference = DisposableCreate(kind,
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
  public void WrappedDeferShouldWork([Values] DeferrerKind kind)
  {
    var i = 1;
    using (new DisposableWrapper(DisposableCreate(kind,
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
  public async Task AsyncDeferShouldWork([Values] DeferrerKind kind)
  {
    var i = 1;
    await using (AsyncDisposableCreate(kind,
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
  public async Task RedundantAsyncDeferShouldWork([Values] DeferrerKind kind)
  {
    var i = 1;

    var defer = AsyncDisposableCreate(kind,
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
  public async Task AsyncDeferResetShouldWork([Values] DeferrerKind? firstKind,
                                              [Values] DeferrerKind? secondKind)
  {
    var first  = false;
    var second = false;

    await using (var disposable = DeferrerCreate(firstKind,
                                                 0,
                                                 () => first = true))
    {
      DeferrerReset(disposable,
                    secondKind,
                    0,
                    () => second = true);
    }

    // Force collection to ensure that previous action is not called
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
    GC.WaitForPendingFinalizers();

    Assert.That(first,
                Is.False);
    if (secondKind is null)
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
  public async Task AsyncDeferShouldBeRaceConditionFree([Values] DeferrerKind kind)
  {
    var i = 1;

    var defer = AsyncDisposableCreate(kind,
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
  public async Task RedundantCopyAsyncDeferShouldWork([Values] DeferrerKind kind)
  {
    var i = 1;

    {
      await using var defer1 = AsyncDisposableCreate(kind,
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
  public void AsyncDeferShouldWorkWhenCollected([Values] DeferrerKind kind)
  {
    var i = 1;

    IAsyncDisposable reference;

    var weakRef = WeakRefAsyncDisposable(() =>
                                         {
                                           reference = AsyncDisposableCreate(kind,
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
  public async Task WrappedAsyncDeferShouldWork([Values] DeferrerKind kind)
  {
    var i = 1;
    await using (new AsyncDisposableWrapper(AsyncDisposableCreate(kind,
                                                                  0,
                                                                  () => i += 1)))
    {
      Assert.That(i,
                  Is.EqualTo(1));
    }

    Assert.That(i,
                Is.EqualTo(2));
  }

  private static Action GetAction(int    delay,
                                  Action f)
  {
    void Action()
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
    }

    return Action;
  }

  private static Func<ValueTask> GetAsyncAction(int    delay,
                                                Action f)
  {
    async ValueTask AsyncAction()
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
    }

    return AsyncAction;
  }

  private static Deferrer DeferrerCreate(DeferrerKind? kind,
                                         int           delay,
                                         Action        f)
    => kind switch
       {
         DeferrerKind.SyncFunc => new Deferrer(GetAction(delay,
                                                         f)),
         DeferrerKind.AsyncFunc => new Deferrer(GetAsyncAction(delay,
                                                               f)),
         DeferrerKind.SyncDisposable => new Deferrer(new DisposableFuncWrapper(GetAction(delay,
                                                                                         f))),
         DeferrerKind.AsyncDisposable => new Deferrer(new AsyncDisposableFuncWrapper(GetAsyncAction(delay,
                                                                                                    f))),
         _ => new Deferrer(),
       };

  private static void DeferrerReset(Deferrer      deferrer,
                                    DeferrerKind? kind,
                                    int           delay,
                                    Action        f)
  {
    switch (kind)
    {
      case null:
        deferrer.Reset();
        break;
      case DeferrerKind.SyncFunc:
        deferrer.Reset(GetAction(delay,
                                 f));
        break;
      case DeferrerKind.AsyncFunc:
        deferrer.Reset(GetAsyncAction(delay,
                                      f));
        break;
      case DeferrerKind.SyncDisposable:
        deferrer.Reset(new DisposableFuncWrapper(GetAction(delay,
                                                           f)));
        break;
      case DeferrerKind.AsyncDisposable:
        deferrer.Reset(new AsyncDisposableFuncWrapper(GetAsyncAction(delay,
                                                                     f)));
        break;
    }
  }

  private static IDisposable DisposableCreate(DeferrerKind? kind,
                                              int           delay,
                                              Action        f)
    => DeferrerCreate(kind,
                      delay,
                      f);

  private static IAsyncDisposable AsyncDisposableCreate(DeferrerKind? kind,
                                                        int           delay,
                                                        Action        f)
    => DeferrerCreate(kind,
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

  private record DisposableFuncWrapper(Action Action) : IDisposable
  {
    public void Dispose()
      => Action();
  }

  private record AsyncDisposableWrapper(IAsyncDisposable AsyncDisposable) : IAsyncDisposable
  {
    public ValueTask DisposeAsync()
      => AsyncDisposable.DisposeAsync();
  }

  private record AsyncDisposableFuncWrapper(Func<ValueTask> Func) : IAsyncDisposable
  {
    public ValueTask DisposeAsync()
      => Func();
  }
}

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

public class DeferTest
{
  [Test]
  public void DeferEmptyShouldWork()
  {
    using var defer = Deferrer.Empty;
  }

  [Test]
  public void DeferShouldWork()
  {
    var i = 1;
    using (Deferrer.Create(() => i += 1))
    {
      Assert.That(i,
                  Is.EqualTo(1));
    }

    Assert.That(i,
                Is.EqualTo(2));
  }


  [Test]
  public void RedundantDeferShouldWork()
  {
    var i = 1;

    var defer = Deferrer.Create(() => i += 1);

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
  public async Task DeferShouldBeRaceConditionFree()
  {
    var i = 1;

    var defer = Deferrer.Create(() =>
                                {
                                  Thread.Sleep(100);
                                  Interlocked.Increment(ref i);
                                });

    var task1 = Task.Run(() => defer.Dispose());
    var task2 = Task.Run(() => defer.Dispose());

    await task1;
    await task2;

    Assert.That(i,
                Is.EqualTo(2));
  }

  [Test]
  public void RedundantCopyDeferShouldWork()
  {
    var i = 1;

    {
      using var defer1 = Deferrer.Create(() => i += 1);
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
  public void DeferShouldWorkWhenCollected()
  {
    var i = 1;

    IDisposable reference;

    var weakRef = WeakRefDisposable(() =>
                                    {
                                      reference = Deferrer.Create(() => i += 1);
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
  public void WrappedDeferShouldWork()
  {
    var i = 1;
    using (new DisposableWrapper(Deferrer.Create(() => i += 1)))
    {
      Assert.That(i,
                  Is.EqualTo(1));
    }

    Assert.That(i,
                Is.EqualTo(2));
  }

  private record DisposableWrapper(IDisposable Disposable) : IDisposable
  {
    public void Dispose()
      => Disposable.Dispose();
  }
}

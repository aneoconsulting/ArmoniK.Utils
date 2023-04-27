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

public class AsyncLazyTest
{
  ///////////////
  // With type //
  ///////////////
  [Test]
  [TestCase(false)]
  [TestCase(true)]
  public async Task AsyncLazyShouldWork(bool async)
  {
    var i = 0;

    var lazy = CreateLazy(async,
                          0,
                          () =>
                          {
                            i += 1;
                            return i;
                          });

    Assert.That(i,
                Is.EqualTo(0));

    var j = await lazy;

    Assert.That(i,
                Is.EqualTo(1));
    Assert.That(j,
                Is.EqualTo(1));

    var k = await lazy;

    Assert.That(i,
                Is.EqualTo(1));
    Assert.That(k,
                Is.EqualTo(1));
  }

  [Test]
  [TestCase(false)]
  [TestCase(true)]
  public async Task AsyncLazyValueShouldBeAsync(bool async)
  {
    var i = 0;

    var lazy = CreateLazy(async,
                          100,
                          () =>
                          {
                            i += 1;
                            return i;
                          });

    Assert.That(i,
                Is.EqualTo(0));

    var task = lazy.Value;

    Assert.That(i,
                Is.EqualTo(0));

    var j = await task.ConfigureAwait(false);

    Assert.That(i,
                Is.EqualTo(1));
    Assert.That(j,
                Is.EqualTo(1));
  }

  private static AsyncLazy<T> CreateLazy<T>(bool    async,
                                            int     delay,
                                            Func<T> f)
  {
    if (async)
    {
      return new AsyncLazy<T>(async () =>
                              {
                                if (delay == 0)
                                {
                                  await Task.Yield();
                                }
                                else
                                {
                                  await Task.Delay(delay)
                                            .ConfigureAwait(false);
                                }

                                return f();
                              });
    }

    return new AsyncLazy<T>(() =>
                            {
                              if (delay == 0)
                              {
                                Thread.Yield();
                              }
                              else
                              {
                                Thread.Sleep(delay);
                              }

                              return f();
                            });
  }

  //////////////////
  // Without type //
  //////////////////
  [Test]
  [TestCase(false)]
  [TestCase(true)]
  public async Task AsyncLazyUntypedShouldWork(bool async)
  {
    var i = 0;

    var lazy = CreateLazyUntyped(async,
                                 0,
                                 () => i += 1);

    Assert.That(i,
                Is.EqualTo(0));

    await lazy;

    Assert.That(i,
                Is.EqualTo(1));

    await lazy;

    Assert.That(i,
                Is.EqualTo(1));
  }

  [Test]
  [TestCase(false)]
  [TestCase(true)]
  public async Task AsyncLazyUntypedValueShouldBeAsync(bool async)
  {
    var i = 0;

    var lazy = CreateLazyUntyped(async,
                                 100,
                                 () => i += 1);

    Assert.That(i,
                Is.EqualTo(0));

    var task = lazy.Value;

    Assert.That(i,
                Is.EqualTo(0));

    await task.ConfigureAwait(false);

    Assert.That(i,
                Is.EqualTo(1));
  }

  private static AsyncLazy CreateLazyUntyped(bool   async,
                                             int    delay,
                                             Action f)
  {
    if (async)
    {
      return new AsyncLazy(async () =>
                           {
                             if (delay == 0)
                             {
                               await Task.Yield();
                             }
                             else
                             {
                               await Task.Delay(delay)
                                         .ConfigureAwait(false);
                             }

                             f();
                           });
    }

    return new AsyncLazy(() =>
                         {
                           if (delay == 0)
                           {
                             Thread.Yield();
                           }
                           else
                           {
                             Thread.Sleep(delay);
                           }

                           f();
                         });
  }
}

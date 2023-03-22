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
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

namespace ArmoniK.Utils.Tests;

public class ParallelSelectExtTest
{
  [Test]
  [TestCase(-1,
            0)]
  [TestCase(-1,
            1)]
  [TestCase(-1,
            4)]
  [TestCase(-1,
            1000)]
  [TestCase(0,
            0)]
  [TestCase(0,
            1)]
  [TestCase(0,
            4)]
  [TestCase(0,
            100)]
  [TestCase(1,
            0)]
  [TestCase(1,
            1)]
  [TestCase(1,
            4)]
  [TestCase(1,
            10)]
  [TestCase(2,
            0)]
  [TestCase(2,
            1)]
  [TestCase(2,
            4)]
  [TestCase(2,
            20)]
  public async Task ParallelSelectShouldWork(int parallelism,
                                             int n)
  {
    var x = await GenerateInts(n)
                  .ParallelSelect(new ParallelTaskOptions(parallelism),
                                  AsyncIdentity(10))
                  .ToListAsync()
                  .ConfigureAwait(false);
    var y = GenerateInts(n)
      .ToList();
    Assert.That(x,
                Is.EqualTo(y));
  }

  [Test]
  [TestCase(-1,
            0)]
  [TestCase(-1,
            1)]
  [TestCase(-1,
            4)]
  [TestCase(-1,
            1000)]
  [TestCase(0,
            0)]
  [TestCase(0,
            1)]
  [TestCase(0,
            4)]
  [TestCase(0,
            100)]
  [TestCase(1,
            0)]
  [TestCase(1,
            1)]
  [TestCase(1,
            4)]
  [TestCase(1,
            10)]
  [TestCase(2,
            0)]
  [TestCase(2,
            1)]
  [TestCase(2,
            4)]
  [TestCase(2,
            20)]
  public async Task ParallelSelectAsyncShouldWork(int parallelism,
                                                  int n)
  {
    var x = await GenerateIntsAsync(n)
                  .ParallelSelect(new ParallelTaskOptions(parallelism),
                                  AsyncIdentity(10))
                  .ToListAsync()
                  .ConfigureAwait(false);
    var y = GenerateInts(n)
      .ToList();
    Assert.That(x,
                Is.EqualTo(y));
  }

  [Test]
  [TestCase(-1,
            10000)]
  [TestCase(0,
            100)]
  [TestCase(1,
            10)]
  [TestCase(2,
            20)]
  [TestCase(10,
            100)]
  [TestCase(100,
            1000)]
  public async Task ParallelSelectLimitShouldWork(int parallelism,
                                                  int n)
  {
    var counter    = 0;
    var maxCounter = 0;
    var delay      = 100;

    async Task<int> IdentityAsync(int x)
    {
      var count = Interlocked.Increment(ref counter);
      var max   = maxCounter;
      while (count > max)
      {
        max = Interlocked.CompareExchange(ref maxCounter,
                                          count,
                                          max);
      }

      await Task.Delay(delay)
                .ConfigureAwait(false);
      Interlocked.Decrement(ref counter);
      return x;
    }

    var x = await GenerateInts(n)
                  .ParallelSelect(new ParallelTaskOptions(parallelism),
                                  IdentityAsync)
                  .ToListAsync()
                  .ConfigureAwait(false);
    var y = GenerateInts(n)
      .ToList();
    Assert.That(x,
                Is.EqualTo(y));

    switch (parallelism)
    {
      case > 0:
        Assert.That(maxCounter,
                    Is.EqualTo(parallelism));
        break;
      case 0:
        Assert.That(maxCounter,
                    Is.EqualTo(Environment.ProcessorCount));
        break;
      case < 0:
        Assert.That(maxCounter,
                    Is.EqualTo(n));
        break;
    }
  }

  [Test]
  [TestCase(-1,
            10000)]
  [TestCase(0,
            100)]
  [TestCase(1,
            10)]
  [TestCase(2,
            20)]
  [TestCase(10,
            100)]
  [TestCase(100,
            1000)]
  public async Task ParallelSelectAsyncLimitShouldWork(int parallelism,
                                                       int n)
  {
    var counter    = 0;
    var maxCounter = 0;
    var delay      = 100;

    async Task<int> IdentityAsync(int x)
    {
      var count = Interlocked.Increment(ref counter);
      var max   = maxCounter;
      while (count > maxCounter)
      {
        max = Interlocked.CompareExchange(ref maxCounter,
                                          count,
                                          max);
      }

      await Task.Delay(delay)
                .ConfigureAwait(false);
      Interlocked.Decrement(ref counter);
      return x;
    }

    var x = await GenerateIntsAsync(n)
                  .ParallelSelect(new ParallelTaskOptions(parallelism),
                                  IdentityAsync)
                  .ToListAsync()
                  .ConfigureAwait(false);
    var y = GenerateInts(n)
      .ToList();

    switch (parallelism)
    {
      case > 0:
        Assert.That(maxCounter,
                    Is.EqualTo(parallelism));
        break;
      case 0:
        Assert.That(maxCounter,
                    Is.EqualTo(Environment.ProcessorCount));
        break;
      case < 0:
        Assert.That(maxCounter,
                    Is.EqualTo(n));
        break;
    }

    Assert.That(x,
                Is.EqualTo(y));
  }

  [Test]
  public async Task UnorderedCompletionShouldWork()
  {
    var firstDone = false;
    var x = await GenerateInts(10000)
                  .ParallelSelect(new ParallelTaskOptions(2),
                                  async i =>
                                  {
                                    if (i != 0)
                                    {
                                      return !firstDone;
                                    }

                                    await Task.Delay(1000)
                                              .ConfigureAwait(false);
                                    firstDone = true;
                                    return true;
                                  })
                  .ToListAsync()
                  .ConfigureAwait(false);
    Assert.That(x,
                Is.All.True);
  }

  [Test]
  public async Task UnorderedAsyncCompletionShouldWork()
  {
    var firstDone = false;
    var x = await GenerateIntsAsync(20,
                                    1)
                  .ParallelSelect(new ParallelTaskOptions(2),
                                  async i =>
                                  {
                                    if (i == 0)
                                    {
                                      await Task.Delay(1000);
                                      firstDone = true;
                                      return true;
                                    }

                                    return !firstDone;
                                  })
                  .ToListAsync()
                  .ConfigureAwait(false);
    Assert.That(x,
                Is.All.True);
  }


  private static IEnumerable<int> GenerateInts(int n)
  {
    for (var i = 0; i < n; ++i)
    {
      yield return i;
    }
  }

  private static async IAsyncEnumerable<int> GenerateIntsAsync(int                                        n,
                                                               int                                        delay             = 0,
                                                               [EnumeratorCancellation] CancellationToken cancellationToken = default)
  {
    for (var i = 0; i < n; ++i)
    {
      if (delay > 0)
      {
        await Task.Delay(delay,
                         cancellationToken)
                  .ConfigureAwait(false);
      }
      else
      {
        await Task.Yield();
      }

      yield return i;
    }
  }

  private static Func<int, Task<int>> AsyncIdentity(int               delay,
                                                    CancellationToken cancellationToken = default)
    => async x =>
       {
         if (delay > 0)
         {
           await Task.Delay(delay,
                            cancellationToken)
                     .ConfigureAwait(false);
         }
         else
         {
           await Task.Yield();
         }

         return x;
       };
}
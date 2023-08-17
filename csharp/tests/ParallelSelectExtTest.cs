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
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

namespace ArmoniK.Utils.Tests;

public class ParallelSelectExtTest
{
  private static readonly bool[] Booleans =
  {
    false,
    true,
  };

  private static IEnumerable ParallelSelectCases()
  {
    var parallelisms = new int?[]
                       {
                         null,
                         -1,
                         0,
                         1,
                         2,
                       };
    var sizes = new[]
                {
                  0,
                  1,
                  4,
                };

    TestCaseData Case(bool blocking,
                      bool useAsync,
                      int? parallelism,
                      int  size)
      => new TestCaseData(blocking,
                          useAsync,
                          parallelism,
                          size).SetArgDisplayNames($"int[{size}], {parallelism}{(useAsync ? ", async" : "")}{(blocking ? ", blocking" : "")}");

    foreach (var blocking in Booleans)
    {
      foreach (var useAsync in Booleans)
      {
        foreach (var parallelism in parallelisms)
        {
          foreach (var size in sizes)
          {
            yield return Case(blocking,
                              useAsync,
                              parallelism,
                              size);
          }
        }

        yield return Case(blocking,
                          useAsync,
                          null,
                          100);
        yield return Case(blocking,
                          useAsync,
                          -1,
                          blocking
                            ? 100
                            : 1000);
        yield return Case(blocking,
                          useAsync,
                          0,
                          100);
        yield return Case(blocking,
                          useAsync,
                          1,
                          10);
        yield return Case(blocking,
                          useAsync,
                          1,
                          20);
      }
    }
  }

  [Test]
  [TestCaseSource(nameof(ParallelSelectCases))]
  public async Task ParallelSelectShouldSucceed(bool blocking,
                                                bool useAsync,
                                                int? parallelism,
                                                int  n)
  {
    var enumerable = GenerateAndSelect(useAsync,
                                       parallelism,
                                       null,
                                       n,
                                       AsyncIdentity(0,
                                                     10,
                                                     blocking));
    var x = await enumerable.ToListAsync()
                            .ConfigureAwait(false);
    var y = GenerateInts(n)
      .ToList();
    Assert.That(x,
                Is.EqualTo(y));
  }

  private static IEnumerable ParallelSelectLimitCases()
  {
    TestCaseData Case(bool blocking,
                      bool useAsync,
                      int? parallelism,
                      int  size)
      => new TestCaseData(blocking,
                          useAsync,
                          parallelism,
                          size).SetArgDisplayNames($"int[{size}], {parallelism}{(useAsync ? ", async" : "")}{(blocking ? ", blocking" : "")}");

    foreach (var blocking in Booleans)
    {
      foreach (var useAsync in Booleans)
      {
        yield return Case(blocking,
                          useAsync,
                          null,
                          100);
        yield return Case(blocking,
                          useAsync,
                          0,
                          100);
        yield return Case(blocking,
                          useAsync,
                          1,
                          10);
        yield return Case(blocking,
                          useAsync,
                          2,
                          20);
        if (!blocking)
        {
          yield return Case(blocking,
                            useAsync,
                            10,
                            100);
          yield return Case(blocking,
                            useAsync,
                            100,
                            1000);
          yield return Case(blocking,
                            useAsync,
                            -1,
                            10000);
        }
      }
    }
  }

  [Test]
  [TestCaseSource(nameof(ParallelSelectLimitCases))]
  [Retry(4)]
  public async Task ParallelSelectLimitShouldSucceed(bool blocking,
                                                     bool useAsync,
                                                     int? parallelism,
                                                     int  n)
  {
    var counter    = 0;
    var maxCounter = 0;

    // We use a larger wait for infinite parallelism to ensure we can actually spawn thousands of tasks in parallel
    var (delayMin, delayMax) = parallelism < 0
                                 ? (500, 1000)
                                 : (50, 100);
    var identity = AsyncIdentity(delayMin,
                                 delayMax,
                                 blocking);

    async Task<int> F(int x)
    {
      var count = Interlocked.Increment(ref counter);
      InterlockedMax(ref maxCounter,
                     count);

      await identity(x)
        .ConfigureAwait(false);
      Interlocked.Decrement(ref counter);
      return x;
    }

    var enumerable = GenerateAndSelect(useAsync,
                                       parallelism,
                                       null,
                                       n,
                                       F);
    var x = await enumerable.ToListAsync()
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
      case null:
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


  private static IEnumerable UnorderedCompletionCases()
    =>
      from useAsync in Booleans
      select new TestCaseData(useAsync).SetArgDisplayNames($"{(useAsync ? "async " : "")}");

  [Test]
  [TestCaseSource(nameof(UnorderedCompletionCases))]
  public async Task UnorderedCompletionShouldSucceed(bool useAsync)
  {
    var firstDone = false;

    async Task<bool> F(int i)
    {
      if (i != 0)
      {
        return !firstDone;
      }

      await Task.Delay(100)
                .ConfigureAwait(false);
      firstDone = true;
      return true;
    }

    var enumerable = GenerateAndSelect(useAsync,
                                       2,
                                       null,
                                       1000,
                                       F);
    var x = await enumerable.ToListAsync()
                            .ConfigureAwait(false);
    Assert.That(x,
                Is.All.True);
  }

  private static IEnumerable CancellationCases()
    =>
      from useAsync in Booleans
      from cancellationAware in Booleans
      from cancelLast in Booleans
      select new TestCaseData(useAsync,
                              cancellationAware,
                              cancelLast)
        .SetArgDisplayNames($"{(cancellationAware ? "aware" : "oblivious")}{(cancelLast ? ", last" : "")}{(useAsync ? ", async" : "")}");

  [Test]
  [TestCaseSource(nameof(CancellationCases))]
  public async Task CancellationShouldSucceed(bool useAsync,
                                              bool cancellationAware,
                                              bool cancelLast)
  {
    const int cancelAt = 100;

    var maxEntered = -1;
    var cts        = new CancellationTokenSource();

    async Task<int> F(int x)
    {
      InterlockedMax(ref maxEntered,
                     x);

      await Task.Delay(100,
                       cancellationAware
                         ? cts.Token
                         : CancellationToken.None)
                .ConfigureAwait(false);
      return x;
    }

    var n = cancelLast
              ? cancelAt + 1
              : 100000;

    int CancelAt(int x)
    {
      if (x == cancelAt)
      {
        cts.Cancel();
      }

      return x;
    }

    var enumerable = useAsync
                       ? GenerateInts(n)
                         .Select(CancelAt)
                         .ParallelSelect(new ParallelTaskOptions(-1,
                                                                 cts.Token),
                                         F)
                       : GenerateIntsAsync(n,
                                           1)
                         .Select(CancelAt)
                         .SelectAwait(async x =>
                                      {
                                        await Task.Yield();
                                        return x;
                                      })
                         .ParallelSelect(new ParallelTaskOptions(-1,
                                                                 cts.Token),
                                         F);

    Assert.That(async () =>
                {
                  await foreach (var _ in enumerable.WithCancellation(CancellationToken.None))
                  {
                  }
                },
                Throws.InstanceOf<OperationCanceledException>());

    await Task.Delay(200,
                     CancellationToken.None)
              .ConfigureAwait(false);

    Assert.That(maxEntered,
                Is.LessThan(cancelAt));
  }

  [Test]
  [TestCaseSource(nameof(CancellationCases))]
  public async Task ThrowingShouldSucceed(bool useAsync,
                                          bool cancellationAware,
                                          bool throwLast)
  {
    const int throwAt = 100;

    async Task<int> F(int x)
    {
      if (x == throwAt)
      {
        throw new ApplicationException();
      }

      await Task.Delay(100)
                .ConfigureAwait(false);
      return x;
    }

    var enumerable = GenerateAndSelect(useAsync,
                                       -1,
                                       null,
                                       throwLast
                                         ? throwAt + 1
                                         : 100000,
                                       F);

    await using var enumerator = enumerable.GetAsyncEnumerator();

    for (var i = 0; i < throwAt; ++i)
    {
      Assert.That(await enumerator.MoveNextAsync()
                                  .ConfigureAwait(false),
                  Is.EqualTo(true));
      Assert.That(enumerator.Current,
                  Is.EqualTo(i));
    }

    Assert.That(enumerator.MoveNextAsync,
                Throws.TypeOf<ApplicationException>());
  }


  [Test]
  [TestCaseSource(nameof(ParallelSelectLimitCases))]
  [Retry(4)]
  public async Task ParallelForeachLimitShouldSucceed(bool blocking,
                                                      bool useAsync,
                                                      int? parallelism,
                                                      int  n)
  {
    var bag        = new ConcurrentBag<int>();
    var counter    = 0;
    var maxCounter = 0;

    // We use a larger wait for infinite parallelism to ensure we can actually spawn thousands of tasks in parallel
    var (delayMin, delayMax) = parallelism < 0
                                 ? (500, 1000)
                                 : (50, 100);
    var identity = AsyncIdentity(delayMin,
                                 delayMax,
                                 blocking);

    async Task F(int x)
    {
      var count = Interlocked.Increment(ref counter);
      InterlockedMax(ref maxCounter,
                     count);

      var y = await identity(x)
                .ConfigureAwait(false);
      bag.Add(y);
      Interlocked.Decrement(ref counter);
    }

    // ReSharper disable function ConvertTypeCheckPatternToNullCheck
    var task = (useAsync, parallelism) switch
               {
                 (false, null) => GenerateInts(n)
                   .ParallelForEach(F),
                 (false, int p) => GenerateInts(n)
                   .ParallelForEach(new ParallelTaskOptions(p),
                                    F),
                 (true, null) => GenerateIntsAsync(n)
                   .ParallelForEach(F),
                 (true, int p) => GenerateIntsAsync(n)
                   .ParallelForEach(new ParallelTaskOptions(p),
                                    F),
               };

    await task.ConfigureAwait(false);

    var x = bag.ToList();
    var y = GenerateInts(n)
      .ToList();

    x.Sort();
    y.Sort();

    Assert.That(x,
                Is.EqualTo(y));

    switch (parallelism)
    {
      case > 0:
        Assert.That(maxCounter,
                    Is.EqualTo(parallelism));
        break;
      case null:
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
  [TestCaseSource(nameof(CancellationCases))]
  public async Task ForeachCancellationShouldSucceed(bool useAsync,
                                                     bool cancellationAware,
                                                     bool cancelLast)
  {
    const int cancelAt = 100;

    var maxEntered = -1;
    var cts        = new CancellationTokenSource();

    async Task F(int x)
    {
      InterlockedMax(ref maxEntered,
                     x);

      await Task.Delay(100,
                       cancellationAware
                         ? cts.Token
                         : CancellationToken.None)
                .ConfigureAwait(false);
    }

    var n = cancelLast
              ? cancelAt + 1
              : 100000;

    int CancelAt(int x)
    {
      if (x == cancelAt)
      {
        cts.Cancel();
      }

      return x;
    }

    var task = useAsync
                 ? GenerateInts(n)
                   .Select(CancelAt)
                   .ParallelForEach(new ParallelTaskOptions(-1,
                                                            cts.Token),
                                    F)
                 : GenerateIntsAsync(n,
                                     1)
                   .Select(CancelAt)
                   .SelectAwait(async x =>
                                {
                                  await Task.Yield();
                                  return x;
                                })
                   .ParallelForEach(new ParallelTaskOptions(-1,
                                                            cts.Token),
                                    F);

    Assert.ThrowsAsync<TaskCanceledException>(async () =>
                                              {
                                                await task.ConfigureAwait(false);
                                              });

    await Task.Delay(200,
                     CancellationToken.None)
              .ConfigureAwait(false);

    Assert.That(maxEntered,
                Is.LessThan(cancelAt));
  }

  private static IAsyncEnumerable<T> GenerateAndSelect<T>(bool               useAsync,
                                                          int?               parallelism,
                                                          CancellationToken? cancellationToken,
                                                          int                n,
                                                          Func<int, Task<T>> f)
  {
    // ReSharper disable function ConvertTypeCheckPatternToNullCheck
    ParallelTaskOptions? options = (parallelism, cancellationToken) switch
                                   {
                                     (null, null)                => null,
                                     (int p, null)               => new ParallelTaskOptions(p),
                                     (null, CancellationToken c) => new ParallelTaskOptions(c),
                                     (int p, CancellationToken c) => new ParallelTaskOptions(p,
                                                                                             c),
                                   };

    return (useAsync, options) switch
           {
             (false, null) => GenerateInts(n)
               .ParallelSelect(f),
             (false, ParallelTaskOptions opt) => GenerateInts(n)
               .ParallelSelect(opt,
                               f),
             (true, null) => GenerateIntsAsync(n)
               .ParallelSelect(f),
             (true, ParallelTaskOptions opt) => GenerateIntsAsync(n)
               .ParallelSelect(opt,
                               f),
           };
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

  private static Func<int, Task<int>> AsyncIdentity(int               delayMin          = 0,
                                                    int               delayMax          = 0,
                                                    bool              blocking          = false,
                                                    CancellationToken cancellationToken = default)
    => async x =>
       {
         if (delayMax <= delayMin)
         {
           delayMax = delayMin + 1;
         }

         var rng = new Random(x);
         var delay = rng.Next(delayMin,
                              delayMax);
         if (delay > 0)
         {
           if (blocking)
           {
             Thread.Sleep(delay);
           }
           else
           {
             await Task.Delay(delay,
                              cancellationToken)
                       .ConfigureAwait(false);
           }
         }
         else
         {
           if (blocking)
           {
             Thread.Yield();
           }
           else
           {
             await Task.Yield();
           }
         }

         return x;
       };

  private static void InterlockedMax(ref int location,
                                     int     value)
  {
    // This is a typical instance of the CAS loop pattern:
    // https://learn.microsoft.com/en-us/dotnet/api/system.threading.interlocked.compareexchange#system-threading-interlocked-compareexchange(system-single@-system-single-system-single)

    // Red the current max at location
    var max = location;

    // repeat as long as current max in less than new value
    while (max < value)
    {
      // Tries to store the new value if max has not changed
      max = Interlocked.CompareExchange(ref location,
                                        value,
                                        max);
    }
  }
}

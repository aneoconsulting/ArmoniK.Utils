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
  private static readonly (int n, int? parallelism)[] SelectCases =
  {
    (0, null),
    (0, -1),
    (0, 0),
    (0, 1),
    (0, 2),
    (1, null),
    (1, -1),
    (1, 0),
    (1, 1),
    (1, 2),
    (4, null),
    (4, -1),
    (4, 0),
    (4, 1),
    (4, 2),
    (10, 1),
    (20, 2),
    (100, null),
    (100, 0),
    (1000, -1),
  };

  public static readonly (int n, int? parallelism)[] SelectLimitCases =
  {
    (10, 1),
    (20, 2),
    (100, null),
    (100, 0),
    (100, 10),
    (1000, 100),
    (10000, -1),
  };

  [Test]
  [AbortAfter(10000)]
  public async Task ParallelSelect([ValueSource(nameof(SelectCases))] (int n, int? parallelism) param,
                                   [Values]                           bool?                     unordered,
                                   [Values]                           bool                      blocking,
                                   [Values]                           bool                      useAsync)
  {
    if (blocking && InvalidBlocking(param))
    {
      return;
    }

    var enumerable = GenerateAndSelect(useAsync,
                                       unordered,
                                       param.parallelism,
                                       null,
                                       param.n,
                                       AsyncIdentity(0,
                                                     10,
                                                     blocking));
    var x = await enumerable.ToListAsync()
                            .ConfigureAwait(false);
    var y = GenerateInts(param.n)
      .ToList();

    if (unordered is true)
    {
      x.Sort();
      y.Sort();
    }

    Assert.That(x,
                Is.EqualTo(y));
  }

  [Test]
  [AbortAfter(100000)]
  [Retry(4)]
  public async Task ParallelSelectLimit([ValueSource(nameof(SelectLimitCases))] (int n, int? parallelism) param,
                                        [Values]                                bool                      unordered,
                                        [Values]                                bool                      blocking,
                                        [Values]                                bool                      useAsync)
  {
    if (blocking && InvalidBlocking(param))
    {
      return;
    }

    var counter    = 0;
    var maxCounter = 0;

    // We use a larger wait for infinite parallelism to ensure we can actually spawn thousands of tasks in parallel
    var (delayMin, delayMax) = param.parallelism < 0
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
                                       unordered,
                                       param.parallelism,
                                       null,
                                       param.n,
                                       F);
    var x = await enumerable.ToListAsync()
                            .ConfigureAwait(false);
    var y = GenerateInts(param.n)
      .ToList();

    if (unordered)
    {
      x.Sort();
      y.Sort();
    }

    Assert.That(x,
                Is.EqualTo(y));

    switch (param.parallelism)
    {
      case > 0:
        Assert.That(maxCounter,
                    Is.EqualTo(param.parallelism));
        break;
      case null:
      case 0:
        Assert.That(maxCounter,
                    Is.EqualTo(Environment.ProcessorCount));
        break;
      case < 0:
        Assert.That(maxCounter,
                    Is.EqualTo(param.n));
        break;
    }
  }

  [Test]
  [AbortAfter(10000)]
  public async Task UnorderedCompletion([Values] bool  useAsync,
                                        [Values] bool? unordered)
  {
    var firstDone = false;

    async Task<int?> F(int i)
    {
      if (i != 0)
      {
        return firstDone
                 ? null
                 : i;
      }

      await Task.Delay(100)
                .ConfigureAwait(false);
      firstDone = true;
      return 0;
    }

    var enumerable = GenerateAndSelect(useAsync,
                                       unordered,
                                       2,
                                       null,
                                       1000,
                                       F);
    var x = await enumerable.ToListAsync()
                            .ConfigureAwait(false);
    var y = GenerateInts(1000)
      .ToList();

    var n = 2;
    if (unordered is true)
    {
      n = 1000;
      Assert.That(x,
                  Has.ItemAt(999)
                     .EqualTo(0));
      x.Sort();
      y.Sort();
    }

    Assert.Multiple(() =>
                    {
                      Assert.That(x.Take(n),
                                  Is.EqualTo(y.Take(n)));
                      Assert.That(x.Skip(n),
                                  Is.All.Null);
                    });
  }

  [Test]
  [AbortAfter(100000)]
  public async Task Cancellation([Values] bool useAsync,
                                 [Values] bool unordered,
                                 [Values] bool cancellationAware,
                                 [Values] bool cancelLast)
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
                         .ParallelSelect(new ParallelTaskOptions(unordered,
                                                                 -1,
                                                                 cts.Token),
                                         F)
                       : GenerateIntsAsync(n,
                                           1,
                                           CancellationToken.None)
                         .Select(CancelAt)
                         .Select(async (int               x,
                                        CancellationToken _) =>
                                 {
                                   await Task.Yield();
                                   return x;
                                 })
                         .ParallelSelect(new ParallelTaskOptions(unordered,
                                                                 -1,
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
  [AbortAfter(10000)]
  public async Task Throwing([Values] bool useAsync,
                             [Values] bool cancellationAware,
                             [Values] bool throwLast)
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
                                       false,
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
  [AbortAfter(10000)]
  public async Task ThrowingUnordered([Values] bool useAsync,
                                      [Values] bool cancellationAware)
  {
    var processorCount = Environment.ProcessorCount;
    var (n, parallelism) = useAsync
                             ? (1000, -1)
                             : (processorCount, processorCount);

    var cts = new CancellationTokenSource();
    var cancellationToken = cancellationAware
                              ? cts.Token
                              : CancellationToken.None;

    var nbFinished = 0;

    async Task<int> F(int x)
    {
      if (x == n - 1)
      {
        throw new ApplicationException();
      }

      await Task.Delay(100,
                       cancellationToken)
                .ConfigureAwait(false);
      Interlocked.Increment(ref x);
      return x;
    }

    var enumerable = GenerateAndSelect(useAsync,
                                       true,
                                       parallelism,
                                       null,
                                       n,
                                       F);

    await using var enumerator = enumerable.GetAsyncEnumerator(CancellationToken.None);

    Assert.That(enumerator.MoveNextAsync,
                Throws.TypeOf<ApplicationException>());

    await Task.Delay(200,
                     CancellationToken.None);
    Assert.That(nbFinished,
                Is.Zero);
  }

  [Test]
  [Repeat(10)]
  [AbortAfter(10000)]
  public async Task CheckReferenceLiveness([Values] bool useAsync,
                                           [Values] bool unordered)
  {
    var weakRefs = new WeakReference?[100];

    Task<object> F(int i)
    {
      object x = i;
      weakRefs[i] = new WeakReference(x);

      return Task.FromResult(x);
    }

    var enumerable = GenerateAndSelect(useAsync,
                                       unordered,
                                       10,
                                       null,
                                       100,
                                       F);

    await using var enumerator = enumerable.GetAsyncEnumerator(CancellationToken.None);

    for (var i = 0; i < 50; ++i)
    {
      await enumerator.MoveNextAsync()
                      .ConfigureAwait(false);
    }

    bool?[] weakRefsAlive;
    do
    {
      // Fist iteration is normally enough,
      // yet to be sure everything was released properly we may loop as much as necessary.
      await Task.Yield();
      GC.Collect();

      weakRefsAlive = weakRefs.Select(weakRef => weakRef?.IsAlive)
                              .ToArray();
    } while (weakRefsAlive.Count(w => w is true) != 11 || weakRefsAlive.Count(w => w is false) != 49);

    if (unordered)
    {
      weakRefsAlive = weakRefsAlive.OrderBy(alive => alive switch
                                                     {
                                                       null  => 2,
                                                       false => 0,
                                                       true  => 1,
                                                     })
                                   .ToArray();
    }

    Assert.Multiple(() =>
                    {
                      // Already fetched results were unreferenced (then not alive)
                      Assert.That(weakRefsAlive.Take(49),
                                  Is.All.False);
                      // Some tasks queued a few more results (max 10), which are then still alive,
                      // the subsequent ones are still null (functor not applied).
                      Assert.That(weakRefsAlive.Skip(49)
                                               .Take(11),
                                  Is.All.True);
                      // The functor was not applied to the remaining elements, then no result is referenced.
                      Assert.That(weakRefsAlive.Skip(60),
                                  Is.All.Null);
                    });
  }


  [Test]
  [AbortAfter(10000)]
  public async Task ParallelForeach([ValueSource(nameof(SelectCases))] (int n, int? parallelism) param,
                                    [Values]                           bool?                     unordered,
                                    [Values]                           bool                      blocking,
                                    [Values]                           bool                      useAsync)
  {
    if (blocking && InvalidBlocking(param))
    {
      return;
    }

    var bag = new ConcurrentBag<int>();
    var identity = AsyncIdentity(0,
                                 10,
                                 blocking);

    async Task F(int x)
    {
      _ = await identity(x);
      bag.Add(x);
    }

    var options = CreateOptions(unordered,
                                param.parallelism,
                                null);

    // ReSharper disable function ConvertTypeCheckPatternToNullCheck
    var task = (useAsync, options) switch
               {
                 (false, null) => GenerateInts(param.n)
                   .ParallelForEach(F),
                 (false, ParallelTaskOptions o) => GenerateInts(param.n)
                   .ParallelForEach(o,
                                    F),
                 (true, null) => GenerateIntsAsync(param.n)
                   .ParallelForEach(F),
                 (true, ParallelTaskOptions o) => GenerateIntsAsync(param.n)
                   .ParallelForEach(o,
                                    F),
               };

    await task.ConfigureAwait(false);

    var x = bag.ToList();
    var y = GenerateInts(param.n)
      .ToList();

    x.Sort();
    y.Sort();

    Assert.That(x,
                Is.EqualTo(y));
  }

  [Test]
  [AbortAfter(100000)]
  [Retry(4)]
  public async Task ParallelForeachLimit([ValueSource(nameof(SelectLimitCases))] (int n, int? parallelism) param,
                                         [Values]                                bool                      unordered,
                                         [Values]                                bool                      blocking,
                                         [Values]                                bool                      useAsync)
  {
    if (blocking && InvalidBlocking(param))
    {
      return;
    }

    var bag        = new ConcurrentBag<int>();
    var counter    = 0;
    var maxCounter = 0;

    // We use a larger wait for infinite parallelism to ensure we can actually spawn thousands of tasks in parallel
    var (delayMin, delayMax) = param.parallelism < 0
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

    var options = CreateOptions(unordered,
                                param.parallelism,
                                null);

    // ReSharper disable function ConvertTypeCheckPatternToNullCheck
    var task = (useAsync, options) switch
               {
                 (false, null) => GenerateInts(param.n)
                   .ParallelForEach(F),
                 (false, ParallelTaskOptions o) => GenerateInts(param.n)
                   .ParallelForEach(o,
                                    F),
                 (true, null) => GenerateIntsAsync(param.n)
                   .ParallelForEach(F),
                 (true, ParallelTaskOptions o) => GenerateIntsAsync(param.n)
                   .ParallelForEach(o,
                                    F),
               };

    await task.ConfigureAwait(false);

    var x = bag.ToList();
    var y = GenerateInts(param.n)
      .ToList();

    x.Sort();
    y.Sort();

    Assert.That(x,
                Is.EqualTo(y));

    switch (param.parallelism)
    {
      case > 0:
        Assert.That(maxCounter,
                    Is.EqualTo(param.parallelism));
        break;
      case null:
      case 0:
        Assert.That(maxCounter,
                    Is.EqualTo(Environment.ProcessorCount));
        break;
      case < 0:
        Assert.That(maxCounter,
                    Is.EqualTo(param.n));
        break;
    }
  }


  [Test]
  [AbortAfter(100000)]
  public async Task ForeachCancellation([Values] bool useAsync,
                                        [Values] bool unordered,
                                        [Values] bool cancellationAware,
                                        [Values] bool cancelLast)
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
                   .ParallelForEach(new ParallelTaskOptions(unordered,
                                                            -1,
                                                            cts.Token),
                                    F)
                 : GenerateIntsAsync(n,
                                     1,
                                     CancellationToken.None)
                   .Select(CancelAt)
                   .Select(async (int               x,
                                  CancellationToken _) =>
                           {
                             await Task.Yield();
                             return x;
                           })
                   .ParallelForEach(new ParallelTaskOptions(unordered,
                                                            -1,
                                                            cts.Token),
                                    F);

    Assert.That(() => task,
                Throws.InstanceOf<OperationCanceledException>());

    await Task.Delay(200,
                     CancellationToken.None)
              .ConfigureAwait(false);

    Assert.That(maxEntered,
                Is.LessThan(cancelAt));
  }

  private static ParallelTaskOptions? CreateOptions(bool?              unordered,
                                                    int?               parallelism,
                                                    CancellationToken? cancellationToken)
    => (unordered, parallelism, cancellationToken) switch
       {
         (null, null, null)   => null,
         (bool u, null, null) => new ParallelTaskOptions(u),
         (null, int p, null)  => new ParallelTaskOptions(p),
         (bool u, int p, null) => new ParallelTaskOptions(u,
                                                          p),
         (null, null, CancellationToken c) => new ParallelTaskOptions(c),
         (bool u, null, CancellationToken c) => new ParallelTaskOptions(u,
                                                                        c),
         (null, int p, CancellationToken c) => new ParallelTaskOptions(p,
                                                                       c),
         (bool u, int p, CancellationToken c) => new ParallelTaskOptions(u,
                                                                         p,
                                                                         c),
       };

  private static IAsyncEnumerable<T> GenerateAndSelect<T>(bool               useAsync,
                                                          bool?              unordered,
                                                          int?               parallelism,
                                                          CancellationToken? cancellationToken,
                                                          int                n,
                                                          Func<int, Task<T>> f)
  {
    // ReSharper disable function ConvertTypeCheckPatternToNullCheck
    var options = CreateOptions(unordered,
                                parallelism,
                                cancellationToken);

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

  private static bool InvalidBlocking((int n, int? parallelism) param)
  {
    var processorCount = Environment.ProcessorCount;
    var parallelism = param.parallelism switch
                      {
                        null => processorCount,
                        0    => processorCount,
                        < 0  => param.n,
                        {
                        } p => Math.Min(p,
                                        param.n),
                      };

    if (parallelism <= Environment.ProcessorCount)
    {
      return false;
    }

    Assert.Ignore("Parallelism is higher than thread count for blocking call");
    return true;
  }

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

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
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

namespace ArmoniK.Utils.Tests;

public class PeekableTest
{
  [Test]
  public void NullSource()
  {
    using var peekable = (null as IEnumerable<int>).AsPeekable();

    Assert.That(peekable.TryPeek(out var value),
                Is.False);
    Assert.That(value,
                Is.Zero);
    Assert.That(() => peekable.Peek(),
                Throws.InstanceOf<InvalidOperationException>());
    Assert.That(peekable.Peek(3),
                Is.Empty);
    Assert.That(peekable.MoveNext(),
                Is.False);
  }

  [Test]
  public void IsItsOwnEnumerator()
  {
    using var peekable = new TrackedSource(3).AsPeekable();

    Assert.That(peekable.GetEnumerator(),
                Is.SameAs(peekable));
    Assert.That(((IEnumerable<int>)peekable).GetEnumerator(),
                Is.SameAs(peekable));
    Assert.That(((IEnumerable)peekable).GetEnumerator(),
                Is.SameAs(peekable));
  }

  [Test]
  public void PeekDoesNotConsume()
  {
    var       source   = new TrackedSource(5);
    using var peekable = source.AsPeekable();

    Assert.That(source.GetEnumeratorCalls,
                Is.Zero,
                "The source should be enumerated lazily");

    Assert.That(peekable.Peek(),
                Is.Zero);
    Assert.That(peekable.Peek(),
                Is.Zero);
    Assert.That(peekable.TryPeek(out var value),
                Is.True);
    Assert.That(value,
                Is.Zero);
    Assert.That(source.MoveNextCalls,
                Is.EqualTo(1));

    Assert.That(peekable.ToList(),
                Is.EqualTo(Enumerable.Range(0,
                                            5)));
    Assert.That(source.GetEnumeratorCalls,
                Is.EqualTo(1));
    Assert.That(source.MoveNextCalls,
                Is.EqualTo(6));
  }

  [Test]
  public void PeekMany()
  {
    using var peekable = new TrackedSource(5).AsPeekable();

    Assert.That(peekable.Peek(0),
                Is.Empty);
    Assert.That(peekable.Peek(3),
                Is.EqualTo(new[]
                           {
                             0,
                             1,
                             2,
                           }));
    Assert.That(peekable.Peek(2),
                Is.EqualTo(new[]
                           {
                             0,
                             1,
                           }));
    Assert.That(peekable.Peek(10),
                Is.EqualTo(Enumerable.Range(0,
                                            5)));
    Assert.That(peekable.Peek(int.MaxValue),
                Is.EqualTo(Enumerable.Range(0,
                                            5)));
    Assert.That(() => peekable.Peek(-1),
                Throws.InstanceOf<ArgumentOutOfRangeException>());
    Assert.That(peekable.ToList(),
                Is.EqualTo(Enumerable.Range(0,
                                            5)));
  }

  [Test]
  public void PeekWhileEnumerating()
  {
    var       source   = new TrackedSource(10);
    using var peekable = source.AsPeekable();

    Assert.That(peekable.Peek(),
                Is.Zero);

    for (var i = 0; i < 5; ++i)
    {
      Assert.That(peekable.MoveNext(),
                  Is.True);
      Assert.That(peekable.Current,
                  Is.EqualTo(i));
      Assert.That(peekable.Peek(),
                  Is.EqualTo(i + 1));
      Assert.That(peekable.Peek(2),
                  Is.EqualTo(new[]
                             {
                               i + 1,
                               i + 2,
                             }));
    }

    // foreach resumes at the current position
    var rest = new List<int>();
    foreach (var x in peekable)
    {
      rest.Add(x);
      if (x == 7)
      {
        Assert.That(peekable.Peek(),
                    Is.EqualTo(8));
        break;
      }
    }

    Assert.That(rest,
                Is.EqualTo(new[]
                           {
                             5,
                             6,
                             7,
                           }));

    // Exiting the foreach disposed the peekable
    Assert.That(source.DisposeCalls,
                Is.EqualTo(1));
    Assert.That(peekable.TryPeek(out _),
                Is.False);
    Assert.That(peekable.MoveNext(),
                Is.False);
  }

  [Test]
  public void Disposal()
  {
    var source   = new TrackedSource(5);
    var peekable = source.AsPeekable();

    Assert.That(peekable.Peek(2),
                Has.Length.EqualTo(2));

    peekable.Dispose();
    peekable.Dispose();

    Assert.That(source.DisposeCalls,
                Is.EqualTo(1));

    // A disposed peekable behaves as exhausted
    Assert.That(() => peekable.Peek(),
                Throws.InstanceOf<InvalidOperationException>());
    Assert.That(peekable.Peek(1),
                Is.Empty);
    Assert.That(peekable.TryPeek(out _),
                Is.False);
    Assert.That(peekable.MoveNext(),
                Is.False);
    Assert.That(source.MoveNextCalls,
                Is.EqualTo(2));
  }

  [Test]
  public void DisposeSourceWhenExhausted()
  {
    var       source   = new TrackedSource(3);
    using var peekable = source.AsPeekable();

    Assert.That(peekable.Peek(5),
                Has.Length.EqualTo(3));
    Assert.That(source.DisposeCalls,
                Is.EqualTo(1));

    // The exhausted source is not called anymore
    Assert.That(peekable.TryPeek(out _),
                Is.True);
    for (var i = 0; i < 3; ++i)
    {
      Assert.That(peekable.MoveNext(),
                  Is.True);
    }

    Assert.That(peekable.TryPeek(out _),
                Is.False);
    Assert.That(peekable.MoveNext(),
                Is.False);
    Assert.That(source.MoveNextCalls,
                Is.EqualTo(4));
    Assert.That(source.DisposeCalls,
                Is.EqualTo(1));
  }

  [Test]
  public void SourceError()
  {
    using var peekable = new TrackedSource(5,
                                           3).AsPeekable();

    Assert.That(() => peekable.Peek(5),
                Throws.InstanceOf<ApplicationException>());

    // Elements read before the error are still available
    for (var i = 0; i < 3; ++i)
    {
      Assert.That(peekable.MoveNext(),
                  Is.True);
      Assert.That(peekable.Current,
                  Is.EqualTo(i));
    }
  }

  [Test]
  public void WrapEnumerator()
  {
    var source = new TrackedSource(5);
    var peekable = source.GetEnumerator()
                         .AsPeekable();

    Assert.That(peekable.Peek(),
                Is.Zero);
    Assert.That(peekable.MoveNext(),
                Is.True);
    Assert.That(peekable.Current,
                Is.Zero);
    Assert.That(peekable.Peek(3),
                Is.EqualTo(new[]
                           {
                             1,
                             2,
                             3,
                           }));
    Assert.That(peekable.MoveNext(),
                Is.True);
    Assert.That(peekable.Current,
                Is.EqualTo(1));
    Assert.That(() => peekable.Reset(),
                Throws.InstanceOf<NotSupportedException>());

    peekable.Dispose();
    Assert.That(source.DisposeCalls,
                Is.EqualTo(1));
    Assert.That(peekable.MoveNext(),
                Is.False);
  }

  [Test]
  public void WrapNullEnumerator()
    => Assert.That(() => (null as IEnumerator<int>)!.AsPeekable(),
                   Throws.InstanceOf<ArgumentNullException>());

  [Test]
  [TestCase(0)]
  [TestCase(1)]
  [TestCase(2)]
  [TestCase(3)]
  public void RandomOperations(int seed)
  {
    const int n      = 1000;
    var       random = new Random(seed);
    var expected = new Queue<int>(Enumerable.Range(0,
                                                   n));
    using var peekable = new TrackedSource(n).AsPeekable();

    while (expected.Count > 0)
    {
      var count = random.Next(100);
      Assert.That(peekable.Peek(count),
                  Is.EqualTo(expected.Take(count)));

      var consume = random.Next(100);
      for (var i = 0; i < consume && expected.Count > 0; ++i)
      {
        Assert.That(peekable.MoveNext(),
                    Is.True);
        Assert.That(peekable.Current,
                    Is.EqualTo(expected.Dequeue()));
      }
    }

    Assert.That(peekable.MoveNext(),
                Is.False);
  }

  [Test]
  public async Task NullSourceAsync()
  {
    await using var peekable = (null as IAsyncEnumerable<int>).AsPeekableAsync();

    Assert.That(await peekable.TryPeekAsync()
                              .ConfigureAwait(false),
                Is.EqualTo((false, 0)));
    Assert.That(async () => await peekable.PeekAsync()
                                          .ConfigureAwait(false),
                Throws.InstanceOf<InvalidOperationException>());
    Assert.That(await peekable.PeekAsync(3)
                              .ConfigureAwait(false),
                Is.Empty);
    Assert.That(await peekable.MoveNextAsync()
                              .ConfigureAwait(false),
                Is.False);
  }

  [Test]
  public async Task IsItsOwnEnumeratorAsync([Values] bool synchronous)
  {
    await using var peekable = new TrackedSource(3)
                               {
                                 Synchronous = synchronous,
                               }.AsPeekableAsync();

    Assert.That(peekable.GetAsyncEnumerator(),
                Is.SameAs(peekable));
    Assert.That(((IAsyncEnumerable<int>)peekable).GetAsyncEnumerator(),
                Is.SameAs(peekable));
  }

  [Test]
  public async Task PeekDoesNotConsumeAsync([Values] bool synchronous)
  {
    var source = new TrackedSource(5)
                 {
                   Synchronous = synchronous,
                 };
    await using var peekable = source.AsPeekableAsync();

    Assert.That(source.GetEnumeratorCalls,
                Is.Zero,
                "The source should be enumerated lazily");

    Assert.That(await peekable.PeekAsync()
                              .ConfigureAwait(false),
                Is.Zero);
    Assert.That(await peekable.PeekAsync()
                              .ConfigureAwait(false),
                Is.Zero);
    Assert.That(await peekable.TryPeekAsync()
                              .ConfigureAwait(false),
                Is.EqualTo((true, 0)));
    Assert.That(source.MoveNextCalls,
                Is.EqualTo(1));

    Assert.That(await peekable.ToListAsync()
                              .ConfigureAwait(false),
                Is.EqualTo(Enumerable.Range(0,
                                            5)));
    Assert.That(source.GetEnumeratorCalls,
                Is.EqualTo(1));
    Assert.That(source.MoveNextCalls,
                Is.EqualTo(6));
  }

  [Test]
  public async Task PeekManyAsync([Values] bool synchronous)
  {
    await using var peekable = new TrackedSource(5)
                               {
                                 Synchronous = synchronous,
                               }.AsPeekableAsync();

    Assert.That(await peekable.PeekAsync(0)
                              .ConfigureAwait(false),
                Is.Empty);
    Assert.That(await peekable.PeekAsync(3)
                              .ConfigureAwait(false),
                Is.EqualTo(new[]
                           {
                             0,
                             1,
                             2,
                           }));
    Assert.That(await peekable.PeekAsync(10)
                              .ConfigureAwait(false),
                Is.EqualTo(Enumerable.Range(0,
                                            5)));
    Assert.That(() => peekable.PeekAsync(-1),
                Throws.InstanceOf<ArgumentOutOfRangeException>());
    Assert.That(await peekable.ToListAsync()
                              .ConfigureAwait(false),
                Is.EqualTo(Enumerable.Range(0,
                                            5)));
  }

  [Test]
  public async Task PeekWhileEnumeratingAsync([Values] bool synchronous)
  {
    var source = new TrackedSource(10)
                 {
                   Synchronous = synchronous,
                 };
    await using var peekable = source.AsPeekableAsync();

    Assert.That(await peekable.PeekAsync()
                              .ConfigureAwait(false),
                Is.Zero);

    for (var i = 0; i < 5; ++i)
    {
      Assert.That(await peekable.MoveNextAsync()
                                .ConfigureAwait(false),
                  Is.True);
      Assert.That(peekable.Current,
                  Is.EqualTo(i));
      Assert.That(await peekable.PeekAsync()
                                .ConfigureAwait(false),
                  Is.EqualTo(i + 1));
      Assert.That(await peekable.PeekAsync(2)
                                .ConfigureAwait(false),
                  Is.EqualTo(new[]
                             {
                               i + 1,
                               i + 2,
                             }));
    }

    var rest = new List<int>();
    await foreach (var x in peekable)
    {
      rest.Add(x);
      if (x == 7)
      {
        Assert.That(await peekable.PeekAsync()
                                  .ConfigureAwait(false),
                    Is.EqualTo(8));
        break;
      }
    }

    Assert.That(rest,
                Is.EqualTo(new[]
                           {
                             5,
                             6,
                             7,
                           }));

    Assert.That(source.DisposeCalls,
                Is.EqualTo(1));
    Assert.That((await peekable.TryPeekAsync()
                               .ConfigureAwait(false)).Success,
                Is.False);
    Assert.That(await peekable.MoveNextAsync()
                              .ConfigureAwait(false),
                Is.False);
  }

  [Test]
  public async Task DisposalAsync([Values] bool synchronous)
  {
    var source = new TrackedSource(5)
                 {
                   Synchronous = synchronous,
                 };
    var peekable = source.AsPeekableAsync();

    Assert.That(await peekable.PeekAsync(2)
                              .ConfigureAwait(false),
                Has.Length.EqualTo(2));

    await peekable.DisposeAsync()
                  .ConfigureAwait(false);
    await peekable.DisposeAsync()
                  .ConfigureAwait(false);

    Assert.That(source.DisposeCalls,
                Is.EqualTo(1));
    Assert.That(async () => await peekable.PeekAsync()
                                          .ConfigureAwait(false),
                Throws.InstanceOf<InvalidOperationException>());
    Assert.That(await peekable.PeekAsync(1)
                              .ConfigureAwait(false),
                Is.Empty);
    Assert.That((await peekable.TryPeekAsync()
                               .ConfigureAwait(false)).Success,
                Is.False);
    Assert.That(await peekable.MoveNextAsync()
                              .ConfigureAwait(false),
                Is.False);
    Assert.That(source.MoveNextCalls,
                Is.EqualTo(2));
  }

  [Test]
  public async Task DisposeSourceWhenExhaustedAsync([Values] bool synchronous)
  {
    var source = new TrackedSource(3)
                 {
                   Synchronous = synchronous,
                 };
    await using var peekable = source.AsPeekableAsync();

    Assert.That(await peekable.PeekAsync(5)
                              .ConfigureAwait(false),
                Has.Length.EqualTo(3));
    Assert.That(source.DisposeCalls,
                Is.EqualTo(1));
    Assert.That(await peekable.ToListAsync()
                              .ConfigureAwait(false),
                Has.Count.EqualTo(3));
    Assert.That(source.MoveNextCalls,
                Is.EqualTo(4));
    Assert.That(source.DisposeCalls,
                Is.EqualTo(1));
  }

  [Test]
  public async Task SourceErrorAsync([Values] bool synchronous)
  {
    await using var peekable = new TrackedSource(5,
                                                 3)
                               {
                                 Synchronous = synchronous,
                               }.AsPeekableAsync();

    Assert.That(async () => await peekable.PeekAsync(5)
                                          .ConfigureAwait(false),
                Throws.InstanceOf<ApplicationException>());

    for (var i = 0; i < 3; ++i)
    {
      Assert.That(await peekable.MoveNextAsync()
                                .ConfigureAwait(false),
                  Is.True);
      Assert.That(peekable.Current,
                  Is.EqualTo(i));
    }
  }

  [Test]
  public async Task WrapEnumeratorAsync([Values] bool synchronous)
  {
    var source = new TrackedSource(5)
                 {
                   Synchronous = synchronous,
                 };
    var peekable = source.GetAsyncEnumerator()
                         .AsPeekableAsync();

    Assert.That(await peekable.PeekAsync()
                              .ConfigureAwait(false),
                Is.Zero);
    Assert.That(await peekable.MoveNextAsync()
                              .ConfigureAwait(false),
                Is.True);
    Assert.That(peekable.Current,
                Is.Zero);
    Assert.That(await peekable.PeekAsync(3)
                              .ConfigureAwait(false),
                Is.EqualTo(new[]
                           {
                             1,
                             2,
                             3,
                           }));
    Assert.That(await peekable.MoveNextAsync()
                              .ConfigureAwait(false),
                Is.True);
    Assert.That(peekable.Current,
                Is.EqualTo(1));

    await peekable.DisposeAsync()
                  .ConfigureAwait(false);
    Assert.That(source.DisposeCalls,
                Is.EqualTo(1));
    Assert.That(await peekable.MoveNextAsync()
                              .ConfigureAwait(false),
                Is.False);
  }

  [Test]
  public async Task SourceCancellationTokenAsync([Values] bool synchronous)
  {
    using var cts = new CancellationTokenSource();
    var source = new TrackedSource(5)
                 {
                   Synchronous = synchronous,
                 };

    await using (var peekable = source.AsPeekableAsync(cts.Token))
    {
      using var other = new CancellationTokenSource();
      Assert.That(await CollectAsync(peekable.WithCancellation(other.Token))
                    .ConfigureAwait(false),
                  Has.Count.EqualTo(5));
      Assert.That(source.CancellationToken,
                  Is.EqualTo(cts.Token),
                  "The construction token should have priority");
    }

    source = new TrackedSource(5)
             {
               Synchronous = synchronous,
             };
    await using (var peekable = source.AsPeekableAsync())
    {
      Assert.That(await CollectAsync(peekable.WithCancellation(cts.Token))
                    .ConfigureAwait(false),
                  Has.Count.EqualTo(5));
      Assert.That(source.CancellationToken,
                  Is.EqualTo(cts.Token),
                  "The enumeration token should be used when none was given at construction");
    }

    source = new TrackedSource(5)
             {
               Synchronous = synchronous,
             };
    await using (var peekable = source.AsPeekableAsync())
    {
      Assert.That(await peekable.PeekAsync()
                                .ConfigureAwait(false),
                  Is.Zero);
      Assert.That(await CollectAsync(peekable.WithCancellation(cts.Token))
                    .ConfigureAwait(false),
                  Has.Count.EqualTo(5));
      Assert.That(source.CancellationToken,
                  Is.EqualTo(CancellationToken.None),
                  "The enumeration token cannot be given to a source already started");
    }
  }

  [Test]
  public async Task EnumerationCancellationAsync([Values] bool synchronous)
  {
    var source = new TrackedSource(5)
                 {
                   Synchronous = synchronous,
                 };
    await using var peekable = source.AsPeekableAsync();
    using var       cts      = new CancellationTokenSource();
    var             received = new List<int>();

    Assert.That(async () =>
                {
                  await foreach (var x in peekable.WithCancellation(cts.Token))
                  {
                    received.Add(x);
                    if (x == 1)
                    {
                      cts.Cancel();
                    }
                  }
                },
                Throws.InstanceOf<OperationCanceledException>());

    Assert.That(received,
                Is.EqualTo(new[]
                           {
                             0,
                             1,
                           }));
    Assert.That(source.DisposeCalls,
                Is.EqualTo(1));
  }

  [Test]
  public void SynchronousCompletion()
  {
    var source = new TrackedSource(5)
                 {
                   Synchronous = true,
                 };
    var peekable = source.AsPeekableAsync();

    AssertSync(peekable.TryPeekAsync(),
               (true, 0));
    AssertSync(peekable.PeekAsync(),
               0);
    AssertSync(peekable.PeekAsync(2),
               new[]
               {
                 0,
                 1,
               });

    // Consume a buffered element, then one read directly from the source
    for (var i = 0; i < 3; ++i)
    {
      AssertSync(peekable.MoveNextAsync(),
                 true);
      Assert.That(peekable.Current,
                  Is.EqualTo(i));
    }

    // Exhaust the source
    AssertSync(peekable.PeekAsync(10),
               new[]
               {
                 3,
                 4,
               });
    Assert.That(source.DisposeCalls,
                Is.EqualTo(1));

    var dispose = peekable.DisposeAsync();
    Assert.That(dispose.IsCompletedSuccessfully,
                Is.True);
  }

  private static void AssertSync<TResult>(ValueTask<TResult> task,
                                          TResult            expected)
  {
    Assert.That(task.IsCompletedSuccessfully,
                Is.True,
                "The operation should complete synchronously");
    Assert.That(task.Result,
                Is.EqualTo(expected));
  }

  private static async Task<List<int>> CollectAsync(ConfiguredCancelableAsyncEnumerable<int> enumerable)
  {
    var list = new List<int>();
    await foreach (var x in enumerable)
    {
      list.Add(x);
    }

    return list;
  }

  /// <summary>
  ///   Source of integers 0..n-1 recording how it is used, and optionally throwing at a given index
  /// </summary>
  private sealed class TrackedSource : IEnumerable<int>, IAsyncEnumerable<int>
  {
    private readonly int  n_;
    private readonly int? throwAt_;

    public TrackedSource(int  n,
                         int? throwAt = null)
    {
      n_       = n;
      throwAt_ = throwAt;
    }

    /// <summary>
    ///   Whether the async operations complete synchronously
    /// </summary>
    public bool Synchronous { get; init; }

    public int               GetEnumeratorCalls { get; private set; }
    public int               MoveNextCalls      { get; private set; }
    public int               DisposeCalls       { get; private set; }
    public CancellationToken CancellationToken  { get; private set; }

    public IAsyncEnumerator<int> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
      GetEnumeratorCalls++;
      CancellationToken = cancellationToken;
      return new Enumerator(this);
    }

    public IEnumerator<int> GetEnumerator()
    {
      GetEnumeratorCalls++;
      return new Enumerator(this);
    }

    IEnumerator IEnumerable.GetEnumerator()
      => GetEnumerator();

    private sealed class Enumerator : IEnumerator<int>, IAsyncEnumerator<int>
    {
      private readonly TrackedSource source_;

      public Enumerator(TrackedSource source)
        => source_ = source;

      public ValueTask DisposeAsync()
      {
        if (source_.Synchronous)
        {
          Dispose();
          return default;
        }

        return Core();

        async ValueTask Core()
        {
          await Task.Yield();
          Dispose();
        }
      }

      public ValueTask<bool> MoveNextAsync()
      {
        if (source_.Synchronous)
        {
          return new ValueTask<bool>(MoveNext());
        }

        return Core();

        async ValueTask<bool> Core()
        {
          await Task.Yield();
          return MoveNext();
        }
      }

      public int Current { get; private set; } = -1;

      public bool MoveNext()
      {
        source_.MoveNextCalls++;
        if (Current + 1 == source_.throwAt_)
        {
          throw new ApplicationException("Source error");
        }

        if (Current + 1 >= source_.n_)
        {
          return false;
        }

        Current++;
        return true;
      }

      public void Reset()
        => throw new NotSupportedException();

      object IEnumerator.Current
        => Current;

      public void Dispose()
        => source_.DisposeCalls++;
    }
  }
}

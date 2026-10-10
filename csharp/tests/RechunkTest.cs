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
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

namespace ArmoniK.Utils.Tests;

public class RechunkTest
{
  private static IEnumerable RandomCases()
  {
    var bounds = new[]
                 {
                   (1, 1),
                   (1, 3),
                   (2, 2),
                   (2, 5),
                   (3, 4),
                   (4, 7),
                   (5, 20),
                   (7, 8),
                 };
    foreach (var (min, max) in bounds)
    {
      foreach (var maxInputSize in new[]
                                   {
                                     1,
                                     3,
                                     10,
                                     30,
                                   })
      {
        for (var seed = 0; seed < 10; ++seed)
        {
          foreach (var timed in new[]
                                {
                                  false,
                                  true,
                                })
          {
            yield return new TestCaseData(min,
                                          max,
                                          maxInputSize,
                                          seed,
                                          timed).SetArgDisplayNames($"[{min}, {max}], input <= {maxInputSize}, seed {seed}, timed {timed}");
          }
        }
      }
    }
  }

  private static int[][] RandomChunks(int maxInputSize,
                                      int seed)
  {
    var rng  = new Random(seed);
    var next = 0;
    return Enumerable.Range(0,
                            rng.Next(0,
                                     20))
                     .Select(_ => Enumerable.Range(0,
                                                   rng.Next(0,
                                                            maxInputSize + 1))
                                            .Select(_ => next++)
                                            .ToArray())
                     .ToArray();
  }

  private static IAsyncEnumerable<ReadOnlyMemory<int>> ToMemories(IEnumerable<int[]> chunks)
    => chunks.Select(chunk => new ReadOnlyMemory<int>(chunk))
             .ToAsyncEnumerable();

  // Yield all the chunks through a single array that is overwritten after each MoveNextAsync
  private static async IAsyncEnumerable<ReadOnlyMemory<int>> ToReusedMemory(IEnumerable<int[]> chunks)
  {
    var buffer = new int[chunks.Select(chunk => chunk.Length)
                               .DefaultIfEmpty(0)
                               .Max()];
    foreach (var chunk in chunks)
    {
      await Task.Yield();
      chunk.CopyTo(buffer,
                   0);
      yield return new ReadOnlyMemory<int>(buffer,
                                           0,
                                           chunk.Length);
      buffer.AsSpan()
            .Fill(-1);
    }
  }

  // The timed code path is used with a timeout that never expires
  private static IAsyncEnumerable<ReadOnlyMemory<int>> Rechunk(IAsyncEnumerable<ReadOnlyMemory<int>> source,
                                                               int                                   min,
                                                               int                                   max,
                                                               bool                                  timed)
    => timed
         ? source.Rechunk(min,
                          max,
                          TimeSpan.FromHours(1),
                          new ChunkFlusher())
         : source.Rechunk(min,
                          max);

  private static void CheckSizes(IReadOnlyList<int> sizes,
                                 int                min,
                                 int                max)
  {
    for (var i = 0; i < sizes.Count; ++i)
    {
      Assert.That(sizes[i],
                  Is.InRange(i == sizes.Count - 1
                               ? 1
                               : min,
                             max),
                  $"chunk {i}");
    }
  }

  private static (T[] array, int offset) Backing<T>(ReadOnlyMemory<T> memory)
  {
    Assert.That(MemoryMarshal.TryGetArray(memory,
                                          out var segment),
                Is.True);
    return (segment.Array!, segment.Offset);
  }

  [Test]
  [TestCaseSource(nameof(RandomCases))]
  public async Task RechunkPreservesContentAndBounds(int  min,
                                                     int  max,
                                                     int  maxInputSize,
                                                     int  seed,
                                                     bool timed)
  {
    var input = RandomChunks(maxInputSize,
                             seed);

    var output = await Rechunk(ToMemories(input),
                               min,
                               max,
                               timed)
                       .ToListAsync()
                       .ConfigureAwait(false);

    Assert.That(output.SelectMany(chunk => chunk.ToArray()),
                Is.EqualTo(input.SelectMany(chunk => chunk)));
    CheckSizes(output.Select(chunk => chunk.Length)
                     .ToList(),
               min,
               max);
  }

  [Test]
  [TestCaseSource(nameof(RandomCases))]
  public async Task RechunkWithReusedSourceMemory(int  min,
                                                  int  max,
                                                  int  maxInputSize,
                                                  int  seed,
                                                  bool timed)
  {
    var input = RandomChunks(maxInputSize,
                             seed);

    // The source reuses its memory: each chunk must be consumed before the next one
    var output = new List<int[]>();
    await foreach (var chunk in Rechunk(ToReusedMemory(input),
                                        min,
                                        max,
                                        timed)
                     .ConfigureAwait(false))
    {
      output.Add(chunk.ToArray());
    }

    Assert.That(output.SelectMany(chunk => chunk),
                Is.EqualTo(input.SelectMany(chunk => chunk)));
    CheckSizes(output.Select(chunk => chunk.Length)
                     .ToList(),
               min,
               max);
  }

  [Test]
  public async Task ChunksWithinBoundsAreNotCopied()
  {
    var input = new[]
                {
                  new int[4],
                  new int[6],
                  new int[5],
                };

    var output = await ToMemories(input)
                       .Rechunk(4,
                                6)
                       .ToListAsync()
                       .ConfigureAwait(false);

    Assert.That(output.Select(Backing),
                Is.EqualTo(input.Select(array => (array, 0))));
  }

  [TestCase(60,
            150,
            new[]
            {
              0,
              90,
            },
            new[]
            {
              90,
              60,
            })]
  [TestCase(60,
            250,
            new[]
            {
              0,
              100,
              190,
            },
            new[]
            {
              100,
              90,
              60,
            })]
  [TestCase(40,
            230,
            new[]
            {
              0,
              100,
              190,
            },
            new[]
            {
              100,
              90,
              40,
            })]
  public async Task LargeChunksAreSlicedWithoutCopy(int   min,
                                                    int   length,
                                                    int[] offsets,
                                                    int[] lengths)
  {
    var input = new int[length];

    var output = await ToMemories(new[]
                                  {
                                    input,
                                  })
                       .Rechunk(min,
                                100)
                       .ToListAsync()
                       .ConfigureAwait(false);

    Assert.That(output.Select(chunk => (Backing(chunk), chunk.Length)),
                Is.EqualTo(offsets.Zip(lengths,
                                       (offset,
                                        len) => ((input, offset), len))));
  }

  [Test]
  public async Task SmallChunksAreMerged()
  {
    var input = new[]
                {
                  new[]
                  {
                    0,
                  },
                  new[]
                  {
                    1,
                    2,
                  },
                  new[]
                  {
                    3,
                    4,
                    5,
                    6,
                    7,
                    8,
                    9,
                  },
                };

    // 1 + 2 < 4: one element is taken from the third chunk, the remaining 6 are not copied
    var output = await ToMemories(input)
                       .Rechunk(4,
                                6)
                       .ToListAsync()
                       .ConfigureAwait(false);

    Assert.That(output.Select(chunk => chunk.ToArray()),
                Is.EqualTo(new[]
                           {
                             new[]
                             {
                               0,
                               1,
                               2,
                               3,
                             },
                             new[]
                             {
                               4,
                               5,
                               6,
                               7,
                               8,
                               9,
                             },
                           }));
    Assert.That(Backing(output[1]),
                Is.EqualTo((input[2], 1)));
  }

  [Test]
  public async Task TooSmallRemainderIsAbsorbed()
  {
    var input = new[]
                {
                  new[]
                  {
                    0,
                    1,
                  },
                  new[]
                  {
                    2,
                    3,
                    4,
                    5,
                  },
                };

    // The remaining 2 elements could not be yielded alone: they are absorbed
    var output = await ToMemories(input)
                       .Rechunk(4,
                                6)
                       .ToListAsync()
                       .ConfigureAwait(false);

    Assert.That(output.Select(chunk => chunk.ToArray()),
                Is.EqualTo(new[]
                           {
                             Enumerable.Range(0,
                                              6)
                                       .ToArray(),
                           }));
  }

  [Test]
  public async Task EmptyChunksAreIgnored()
  {
    var output = await ToMemories(new[]
                                  {
                                    Array.Empty<int>(),
                                    Array.Empty<int>(),
                                  })
                       .Rechunk(1,
                                1)
                       .ToListAsync()
                       .ConfigureAwait(false);

    Assert.That(output,
                Is.Empty);
  }

  [Test]
  public async Task NullSourceIsEmpty()
  {
    var output = await ((IAsyncEnumerable<ReadOnlyMemory<int>>?)null).Rechunk(1,
                                                                              1)
                                                                     .ToListAsync()
                                                                     .ConfigureAwait(false);

    Assert.That(output,
                Is.Empty);
  }

  [TestCase(0,
            1,
            "chunkMinSize")]
  [TestCase(-1,
            1,
            "chunkMinSize")]
  [TestCase(2,
            1,
            "chunkMaxSize")]
  public void InvalidBoundsThrow(int    min,
                                 int    max,
                                 string paramName)
    => Assert.That(() => ToMemories(Array.Empty<int[]>())
                     .Rechunk(min,
                              max),
                   Throws.InstanceOf<ArgumentOutOfRangeException>()
                         .With.Property(nameof(ArgumentException.ParamName))
                         .EqualTo(paramName));

  [Test]
  public async Task BufferedDataIsYieldedBeforeException()
  {
    static async IAsyncEnumerable<ReadOnlyMemory<int>> Throwing()
    {
      await Task.Yield();
      yield return new[]
                   {
                     0,
                   };
      throw new ApplicationException();
    }

    await using var enumerator = Throwing()
                                 .Rechunk(2,
                                          2)
                                 .GetAsyncEnumerator();

    Assert.That(await enumerator.MoveNextAsync()
                                .ConfigureAwait(false),
                Is.True);
    Assert.That(enumerator.Current.ToArray(),
                Is.EqualTo(new[]
                           {
                             0,
                           }));
    Assert.That(async () => await enumerator.MoveNextAsync()
                                            .ConfigureAwait(false),
                Throws.InstanceOf<ApplicationException>());
  }

  [Test]
  public async Task CancellationYieldsReceivedDataWithoutFetchingMore()
  {
    using var cts     = new CancellationTokenSource();
    var       fetched = 0;

    async IAsyncEnumerable<ReadOnlyMemory<int>> Source()
    {
      await Task.Yield();
      fetched++;
      yield return Enumerable.Range(0,
                                    7)
                             .ToArray();
      fetched++;
      yield return new int[2];
    }

    var output = new List<int[]>();
    Assert.That(async () =>
                {
                  await foreach (var chunk in Source()
                                              .Rechunk(2,
                                                       2)
                                              .WithCancellation(cts.Token)
                                              .ConfigureAwait(false))
                  {
                    output.Add(chunk.ToArray());
                    cts.Cancel();
                  }
                },
                Throws.InstanceOf<OperationCanceledException>());

    // The first input chunk is entirely yielded, but the second one is never fetched
    Assert.That(output,
                Is.EqualTo(new[]
                           {
                             new[]
                             {
                               0,
                               1,
                             },
                             new[]
                             {
                               2,
                               3,
                             },
                             new[]
                             {
                               4,
                               5,
                             },
                             new[]
                             {
                               6,
                             },
                           }));
    Assert.That(fetched,
                Is.EqualTo(1));
  }

  [Test]
  public async Task CancellationIsForwardedToSource()
  {
    using var cts      = new CancellationTokenSource();
    var       received = CancellationToken.None;

    async IAsyncEnumerable<ReadOnlyMemory<int>> Source([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
      await Task.Yield();
      received = cancellationToken;
      yield return new int[1];
    }

    await using var enumerator = Source()
                                 .Rechunk(1,
                                          1)
                                 .GetAsyncEnumerator(cts.Token);

    Assert.That(await enumerator.MoveNextAsync()
                                .ConfigureAwait(false),
                Is.True);
    Assert.That(received,
                Is.EqualTo(cts.Token));

    cts.Cancel();
    Assert.That(async () => await enumerator.MoveNextAsync()
                                            .ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
  }

  [Test]
  public async Task SourceIsDisposed()
  {
    var disposed = false;

    async IAsyncEnumerable<ReadOnlyMemory<int>> Source()
    {
      try
      {
        await Task.Yield();
        yield return new int[1];
        yield return new int[1];
      }
      finally
      {
        disposed = true;
      }
    }

    await foreach (var _ in Source()
                            .Rechunk(1,
                                     1)
                            .ConfigureAwait(false))
    {
      break;
    }

    Assert.That(disposed,
                Is.True);
  }

  // -1 ms is Timeout.InfiniteTimeSpan, which is valid
  [TestCase(-2)]
  [TestCase(30L * 24 * 3600 * 1000)]
  public void InvalidMaxDelayThrows(long delayMs)
    => Assert.That(() => ToMemories(Array.Empty<int[]>())
                     .Rechunk(1,
                              1,
                              TimeSpan.FromMilliseconds(delayMs)),
                   Throws.InstanceOf<ArgumentOutOfRangeException>()
                         .With.Property(nameof(ArgumentException.ParamName))
                         .EqualTo("maxDelay"));

  // Source yielding the given chunks, then waiting for the gate before yielding the last ones
  private static async IAsyncEnumerable<ReadOnlyMemory<int>> Gated(int[][]                                    before,
                                                                   Task                                       gate,
                                                                   int[][]                                    after,
                                                                   [EnumeratorCancellation] CancellationToken cancellationToken = default)
  {
    await Task.Yield();
    foreach (var chunk in before)
    {
      yield return chunk;
    }

    // Task.WaitAsync is not available on .NET Framework
    await Task.WhenAny(gate,
                       Task.Delay(Timeout.Infinite,
                                  cancellationToken))
              .ConfigureAwait(false);
    cancellationToken.ThrowIfCancellationRequested();
    foreach (var chunk in after)
    {
      yield return chunk;
    }
  }

  [Test]
  [AbortAfter(10000)]
  public async Task FlushYieldsBufferedData()
  {
    var flusher = new ChunkFlusher();
    var gate    = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

    await using var enumerator = Gated(new[]
                                       {
                                         new[]
                                         {
                                           0,
                                           1,
                                         },
                                       },
                                       gate.Task,
                                       new[]
                                       {
                                         new[]
                                         {
                                           2,
                                           3,
                                           4,
                                         },
                                       })
                                 .Rechunk(4,
                                          8,
                                          flusher: flusher)
                                 .GetAsyncEnumerator();

    // [0, 1] is buffered while the source is waiting
    var move = enumerator.MoveNextAsync();
    await Task.Delay(100)
              .ConfigureAwait(false);
    Assert.That(move.IsCompleted,
                Is.False);

    flusher.Flush();
    Assert.That(await move.ConfigureAwait(false),
                Is.True);
    Assert.That(enumerator.Current.ToArray(),
                Is.EqualTo(new[]
                           {
                             0,
                             1,
                           }));

    gate.SetResult(true);
    Assert.That(await enumerator.MoveNextAsync()
                                .ConfigureAwait(false),
                Is.True);
    Assert.That(enumerator.Current.ToArray(),
                Is.EqualTo(new[]
                           {
                             2,
                             3,
                             4,
                           }));
    Assert.That(await enumerator.MoveNextAsync()
                                .ConfigureAwait(false),
                Is.False);
  }

  [Test]
  [AbortAfter(10000)]
  public async Task FlushWithEmptyBufferIsIgnored()
  {
    var flusher = new ChunkFlusher();
    var gate    = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

    await using var enumerator = Gated(new[]
                                       {
                                         new[]
                                         {
                                           0,
                                         },
                                       },
                                       gate.Task,
                                       Array.Empty<int[]>())
                                 .Rechunk(4,
                                          8,
                                          flusher: flusher)
                                 .GetAsyncEnumerator();

    // Nothing is buffered yet: ignored
    flusher.Flush();

    var move = enumerator.MoveNextAsync();
    await Task.Delay(100)
              .ConfigureAwait(false);
    Assert.That(move.IsCompleted,
                Is.False);

    flusher.Flush();
    Assert.That(await move.ConfigureAwait(false),
                Is.True);
    Assert.That(enumerator.Current.ToArray(),
                Is.EqualTo(new[]
                           {
                             0,
                           }));

    gate.SetResult(true);
    Assert.That(await enumerator.MoveNextAsync()
                                .ConfigureAwait(false),
                Is.False);
  }

  [Test]
  [AbortAfter(10000)]
  public async Task TimeoutYieldsBufferedData()
  {
    var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

    await using var enumerator = Gated(new[]
                                       {
                                         new[]
                                         {
                                           0,
                                           1,
                                         },
                                       },
                                       gate.Task,
                                       Array.Empty<int[]>())
                                 .Rechunk(4,
                                          8,
                                          TimeSpan.FromMilliseconds(100))
                                 .GetAsyncEnumerator();

    Assert.That(await enumerator.MoveNextAsync()
                                .ConfigureAwait(false),
                Is.True);
    Assert.That(enumerator.Current.ToArray(),
                Is.EqualTo(new[]
                           {
                             0,
                             1,
                           }));

    gate.SetResult(true);
    Assert.That(await enumerator.MoveNextAsync()
                                .ConfigureAwait(false),
                Is.False);
  }

  [Test]
  [AbortAfter(10000)]
  public async Task DeadlineStartsAtInputArrival()
  {
    var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

    // A slice of 4 is yielded, and the last element is buffered
    await using var enumerator = Gated(new[]
                                       {
                                         Enumerable.Range(0,
                                                          5)
                                                   .ToArray(),
                                       },
                                       gate.Task,
                                       Array.Empty<int[]>())
                                 .Rechunk(3,
                                          4,
                                          TimeSpan.FromMilliseconds(1000))
                                 .GetAsyncEnumerator();

    Assert.That(await enumerator.MoveNextAsync()
                                .ConfigureAwait(false),
                Is.True);
    Assert.That(enumerator.Current.Length,
                Is.EqualTo(4));

    // The consumer is slower than maxDelay: the remainder is already due
    await Task.Delay(1100)
              .ConfigureAwait(false);

    var sw = Stopwatch.StartNew();
    Assert.That(await enumerator.MoveNextAsync()
                                .ConfigureAwait(false),
                Is.True);
    sw.Stop();
    Assert.That(enumerator.Current.ToArray(),
                Is.EqualTo(new[]
                           {
                             4,
                           }));
    Assert.That(sw.Elapsed,
                Is.LessThan(TimeSpan.FromMilliseconds(500)));

    gate.SetResult(true);
    Assert.That(await enumerator.MoveNextAsync()
                                .ConfigureAwait(false),
                Is.False);
  }

  [Test]
  [AbortAfter(10000)]
  public async Task AvailableDataIsMergedAfterDeadline()
  {
    var flusher = new ChunkFlusher();

    // The source is always ready: available data is merged before flushing
    async IAsyncEnumerable<ReadOnlyMemory<int>> Source()
    {
      await Task.Yield();
      yield return new[]
                   {
                     0,
                   };
      flusher.Flush();
      yield return new[]
                   {
                     1,
                   };
      yield return new[]
                   {
                     2,
                   };
    }

    var output = await Source()
                       .Rechunk(4,
                                8,
                                flusher: flusher)
                       .ToListAsync()
                       .ConfigureAwait(false);

    // The flush is observed after merging the next input chunk
    Assert.That(output.Select(chunk => chunk.ToArray()),
                Is.EqualTo(new[]
                           {
                             new[]
                             {
                               0,
                               1,
                             },
                             new[]
                             {
                               2,
                             },
                           }));
  }

  [Test]
  [AbortAfter(10000)]
  [TestCase(true)]
  [TestCase(false)]
  public async Task EarlyStopWhileSourceIsFetching(bool cancellable)
  {
    var disposed = false;

    async IAsyncEnumerable<ReadOnlyMemory<int>> Source([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
      try
      {
        await Task.Yield();
        yield return new[]
                     {
                       0,
                     };
        await Task.Delay(cancellable
                           ? Timeout.Infinite
                           : 200,
                         cancellable
                           ? cancellationToken
                           : CancellationToken.None)
                  .ConfigureAwait(false);
        yield return new[]
                     {
                       1,
                     };
      }
      finally
      {
        disposed = true;
      }
    }

    // Yielded on timeout, while the source is still fetching
    await foreach (var chunk in Source()
                                .Rechunk(4,
                                         8,
                                         TimeSpan.FromMilliseconds(50))
                                .ConfigureAwait(false))
    {
      Assert.That(chunk.ToArray(),
                  Is.EqualTo(new[]
                             {
                               0,
                             }));
      break;
    }

    Assert.That(disposed,
                Is.True);
  }

  [Test]
  [AbortAfter(10000)]
  public async Task FlushNeverContinuesOnCallerThread()
  {
    var flusher = new ChunkFlusher();
    var gate    = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

    await using var enumerator = Gated(new[]
                                       {
                                         new[]
                                         {
                                           0,
                                         },
                                       },
                                       gate.Task,
                                       Array.Empty<int[]>())
                                 .Rechunk(4,
                                          8,
                                          flusher: flusher)
                                 .GetAsyncEnumerator();

    var move = enumerator.MoveNextAsync()
                         .AsTask();
    await Task.Delay(50)
              .ConfigureAwait(false);

    // Completed here if the enumeration continued on the thread calling Flush
    var completedOnFlushThread = false;
    var thread = new Thread(() =>
                            {
                              flusher.Flush();
                              completedOnFlushThread = move.IsCompleted;
                            });
    thread.Start();
    thread.Join();

    Assert.That(await move.ConfigureAwait(false),
                Is.True);
    Assert.That(completedOnFlushThread,
                Is.False);
    Assert.That(enumerator.Current.ToArray(),
                Is.EqualTo(new[]
                           {
                             0,
                           }));

    gate.SetResult(true);
    Assert.That(await enumerator.MoveNextAsync()
                                .ConfigureAwait(false),
                Is.False);
  }

  [Test]
  [AbortAfter(10000)]
  public async Task SourceErrorAfterEarlyYield()
  {
    // Fails after the buffered data has been yielded on timeout
    static async IAsyncEnumerable<ReadOnlyMemory<int>> Source()
    {
      await Task.Yield();
      yield return new[]
                   {
                     0,
                   };
      await Task.Delay(200)
                .ConfigureAwait(false);
      throw new ApplicationException();
    }

    await using var enumerator = Source()
                                 .Rechunk(4,
                                          8,
                                          TimeSpan.FromMilliseconds(50))
                                 .GetAsyncEnumerator();

    Assert.That(await enumerator.MoveNextAsync()
                                .ConfigureAwait(false),
                Is.True);
    Assert.That(enumerator.Current.ToArray(),
                Is.EqualTo(new[]
                           {
                             0,
                           }));
    Assert.That(async () => await enumerator.MoveNextAsync()
                                            .ConfigureAwait(false),
                Throws.InstanceOf<ApplicationException>());
  }

  [Test]
  [AbortAfter(10000)]
  public async Task EarlyStopAfterFlushWhileSourceIsFetching()
  {
    var flusher  = new ChunkFlusher();
    var disposed = false;

    async IAsyncEnumerable<ReadOnlyMemory<int>> Source([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
      try
      {
        await Task.Yield();
        yield return new[]
                     {
                       0,
                     };
        flusher.Flush();
        await Task.Delay(Timeout.Infinite,
                         cancellationToken)
                  .ConfigureAwait(false);
      }
      finally
      {
        disposed = true;
      }
    }

    await foreach (var chunk in Source()
                                .Rechunk(4,
                                         8,
                                         flusher: flusher)
                                .ConfigureAwait(false))
    {
      Assert.That(chunk.ToArray(),
                  Is.EqualTo(new[]
                             {
                               0,
                             }));
      break;
    }

    Assert.That(disposed,
                Is.True);
  }

  // Races between the source, the deadline, the flusher and the consumer
  [Test]
  [AbortAfter(60000)]
  public async Task ConcurrentFlushesStress([Range(0,
                                                   9)]
                                            int seed)
  {
    var rng = new Random(seed);
    for (var iteration = 0; iteration < 30; ++iteration)
    {
      var min = rng.Next(1,
                         8);
      var max = min + rng.Next(0,
                               8);
      var delay = TimeSpan.FromMilliseconds(rng.Next(0,
                                                     3));
      var lengths = Enumerable.Range(0,
                                     rng.Next(0,
                                              40))
                              .Select(_ => rng.Next(0,
                                                    12))
                              .ToArray();
      var stopAfter = rng.Next(0,
                               4) == 0
                        ? rng.Next(0,
                                   10)
                        : int.MaxValue;
      var waits = Enumerable.Range(0,
                                   lengths.Length)
                            .Select(_ => rng.Next(0,
                                                  4))
                            .ToArray();
      var flusher  = new ChunkFlusher();
      var input    = new List<int>();
      var disposed = 0;

      async IAsyncEnumerable<ReadOnlyMemory<int>> Source()
      {
        try
        {
          var next = 0;
          for (var i = 0; i < lengths.Length; ++i)
          {
            switch (waits[i])
            {
              case 0:
                await Task.Yield();
                break;
              case 1:
                await Task.Delay(1)
                          .ConfigureAwait(false);
                break;
            }

            var chunk = Enumerable.Range(next,
                                         lengths[i])
                                  .ToArray();
            next += lengths[i];
            input.AddRange(chunk);
            yield return chunk;
          }
        }
        finally
        {
          Interlocked.Increment(ref disposed);
        }
      }

      using var cts = new CancellationTokenSource();
      var flushing = Task.Run(async () =>
                              {
                                while (!cts.IsCancellationRequested)
                                {
                                  flusher.Flush();
                                  await Task.Delay(1)
                                            .ConfigureAwait(false);
                                }
                              });

      var output = new List<int[]>();
      try
      {
        await foreach (var chunk in Source()
                                    .Rechunk(min,
                                             max,
                                             delay,
                                             flusher)
                                    .ConfigureAwait(false))
        {
          output.Add(chunk.ToArray());
          if (output.Count >= stopAfter)
          {
            break;
          }
        }
      }
      finally
      {
        cts.Cancel();
        await flushing.ConfigureAwait(false);
      }

      var flat = output.SelectMany(chunk => chunk)
                       .ToList();
      Assert.That(disposed,
                  Is.EqualTo(1));
      Assert.That(output.Select(chunk => chunk.Length),
                  Has.All.InRange(1,
                                  max));
      Assert.That(flat,
                  Is.EqualTo(input.Take(flat.Count)));
      if (stopAfter == int.MaxValue)
      {
        Assert.That(flat,
                    Has.Count.EqualTo(input.Count));
      }
    }
  }
}

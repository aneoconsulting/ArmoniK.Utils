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
          yield return new TestCaseData(min,
                                        max,
                                        maxInputSize,
                                        seed).SetArgDisplayNames($"[{min}, {max}], input <= {maxInputSize}, seed {seed}");
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
  public async Task RechunkPreservesContentAndBounds(int min,
                                                     int max,
                                                     int maxInputSize,
                                                     int seed)
  {
    var input = RandomChunks(maxInputSize,
                             seed);

    var output = await ToMemories(input)
                       .Rechunk(min,
                                max)
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
  public async Task RechunkWithReusedSourceMemory(int min,
                                                  int max,
                                                  int maxInputSize,
                                                  int seed)
  {
    var input = RandomChunks(maxInputSize,
                             seed);

    // Each chunk must be consumed before requesting the next one, as the source reuses its memory
    var output = new List<int[]>();
    await foreach (var chunk in ToReusedMemory(input)
                                .Rechunk(min,
                                         max)
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

    // 1 + 2 = 3 < 4: 1 more element is needed from the third chunk, and the remaining 6 are yielded without copy
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

    // 2 + 2 = 4 reaches the minimum, but the remaining 2 elements could not be yielded on their own
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
  public void SourceExceptionIsPropagated()
  {
    static async IAsyncEnumerable<ReadOnlyMemory<int>> Throwing()
    {
      await Task.Yield();
      yield return new int[1];
      throw new ApplicationException();
    }

    Assert.That(async () => await Throwing()
                                  .Rechunk(2,
                                           2)
                                  .ToListAsync()
                                  .ConfigureAwait(false),
                Throws.InstanceOf<ApplicationException>());
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
}

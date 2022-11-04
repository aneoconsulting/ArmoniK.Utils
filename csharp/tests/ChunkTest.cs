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

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

using NUnit.Framework;

namespace ArmoniK.Utils.Tests;

public class ChunkTest
{
  public static IEnumerable ChunkArrayCases(int chunkSize)
  {
    for (var n = 0; n <= 4 * chunkSize; ++n)
    {
      yield return new TestCaseData(Enumerable.Range(0,
                                                     n)
                                              .ToArray(),
                                    chunkSize).SetName($"int[{n}] chunked by {chunkSize}");
    }
  }

  [Test]
  [TestCaseSource(nameof(ChunkArrayCases),
                  new object[]
                  {
                    1,
                  })]
  [TestCaseSource(nameof(ChunkArrayCases),
                  new object[]
                  {
                    2,
                  })]
  [TestCaseSource(nameof(ChunkArrayCases),
                  new object[]
                  {
                    3,
                  })]
  [TestCaseSource(nameof(ChunkArrayCases),
                  new object[]
                  {
                    4,
                  })]
  public void CheckChunkSize(IEnumerable<int> enumerable,
                             int              chunkSize)
  {
    var lastLength = chunkSize;
    foreach (var chunk in enumerable.ToChunks(chunkSize))
    {
      var length = chunk.Length;
      Assert.That(length,
                  Is.InRange(1,
                             lastLength));
      Assert.That(chunkSize,
                  Is.AnyOf(length,
                           lastLength));
      lastLength = length;
    }
  }

  [Test]
  [TestCaseSource(nameof(ChunkArrayCases),
                  new object[]
                  {
                    1,
                  })]
  [TestCaseSource(nameof(ChunkArrayCases),
                  new object[]
                  {
                    2,
                  })]
  [TestCaseSource(nameof(ChunkArrayCases),
                  new object[]
                  {
                    3,
                  })]
  [TestCaseSource(nameof(ChunkArrayCases),
                  new object[]
                  {
                    4,
                  })]
  public void ChunkShouldKeepOrder(int[] array,
                                   int   chunkSize)
  {
    var i = 0;

    foreach (var chunk in array.ToChunks(chunkSize)
                               .ToList())
    {
      foreach (var x in chunk)
      {
        Assert.That(i,
                    Is.LessThan(array.Length));
        Assert.That(x,
                    Is.EqualTo(array[i]));
        i += 1;
      }
    }
  }

  [Test]
  [TestCase(1)]
  [TestCase(2)]
  [TestCase(3)]
  [TestCase(4)]
  public void ChunkNullShouldSucceed(int chunkSize)
  {
    var chunks = (null as IEnumerable<int>).ToChunks(chunkSize);
    // ReSharper disable once PossibleMultipleEnumeration
    Assert.That(chunks,
                Is.Not.Null);
    // ReSharper disable once PossibleMultipleEnumeration
    Assert.That(chunks.Count(),
                Is.Zero);
  }

  [Test]
  [TestCase(null,
            0)]
  [TestCase(null,
            -1)]
  [TestCase(0,
            0)]
  [TestCase(0,
            -1)]
  [TestCase(1,
            0)]
  [TestCase(1,
            -1)]
  public void ChunkByZeroShouldFail(int? arraySize,
                                    int  chunkSize)
  {
    var enumerable = arraySize is not null
                       ? Enumerable.Range(0,
                                          (int)arraySize)
                       : null as IEnumerable<int>;
    Assert.Throws<ArgumentOutOfRangeException>(() => enumerable.ToChunks(chunkSize));
  }
}

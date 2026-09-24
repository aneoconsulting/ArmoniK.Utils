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
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

namespace ArmoniK.Utils.Tests;

public class EnumerableExtTest
{
  [Test]
  public void AsIListNull()
  {
    var list = (null as IEnumerable<int>).AsIList();
    Assert.That(list,
                Is.Not.Null);
    Assert.That(list,
                Is.Empty);
  }

  [Test]
  public void AsICollectionNull()
  {
    var collection = (null as IEnumerable<int>).AsICollection();
    Assert.That(collection,
                Is.Not.Null);
    Assert.That(collection,
                Is.Empty);
  }

  [Test]
  [TestCase(0)]
  [TestCase(1)]
  [TestCase(4)]
  public void AsIListElements(int n)
  {
    var list = GenerateInts(n)
      .AsIList();
    using var enumerator = GenerateInts(n)
      .GetEnumerator();

    foreach (var x in list)
    {
      Assert.That(enumerator.MoveNext(),
                  Is.True);
      var y = enumerator.Current;

      Assert.That(x,
                  Is.EqualTo(y));
    }

    Assert.That(enumerator.MoveNext(),
                Is.False);
  }

  [Test]
  [TestCase(0)]
  [TestCase(1)]
  [TestCase(4)]
  public void AsICollectionElements(int n)
  {
    var collection = GenerateInts(n)
      .AsICollection();
    using var enumerator = GenerateInts(n)
      .GetEnumerator();

    foreach (var x in collection)
    {
      Assert.That(enumerator.MoveNext(),
                  Is.True);
      var y = enumerator.Current;

      Assert.That(x,
                  Is.EqualTo(y));
    }

    Assert.That(enumerator.MoveNext(),
                Is.False);
  }


  [Test]
  [TestCase(0)]
  [TestCase(1)]
  [TestCase(4)]
  public void AsIListReferenceArray(int n)
  {
    var orig = Enumerable.Range(0,
                                n)
                         .ToArray();
    var list = orig.AsIList();
    Assert.That(ReferenceEquals(list,
                                orig));
  }

  [Test]
  [TestCase(0)]
  [TestCase(1)]
  [TestCase(4)]
  public void AsIListReferenceList(int n)
  {
    var orig = Enumerable.Range(0,
                                n)
                         .ToList();
    var list = orig.AsIList();
    Assert.That(ReferenceEquals(list,
                                orig));
  }

  [Test]
  [TestCase(0)]
  [TestCase(1)]
  [TestCase(4)]
  public void AsICollectionReferenceArray(int n)
  {
    var orig = Enumerable.Range(0,
                                n)
                         .ToArray();
    var collection = orig.AsICollection();
    Assert.That(ReferenceEquals(collection,
                                orig));
  }

  [Test]
  [TestCase(0)]
  [TestCase(1)]
  [TestCase(4)]
  public void AsICollectionReferenceList(int n)
  {
    var orig = Enumerable.Range(0,
                                n)
                         .ToList();
    var collection = orig.AsICollection();
    Assert.That(ReferenceEquals(collection,
                                orig));
  }

  [Test]
  [TestCase(0)]
  [TestCase(1)]
  [TestCase(4)]
  public void AsICollectionReferenceHashSet(int n)
  {
    var orig = new HashSet<int>(Enumerable.Range(0,
                                                 n));
    var collection = orig.AsICollection();
    Assert.That(ReferenceEquals(collection,
                                orig));
  }

  [Test]
  public void ToBlockingEnumerable()
    => Assert.That(GenerateInts(5)
                   .ToAsync()
                   .ToBlocking(),
                   Is.EqualTo(GenerateInts(5)));

  [Test]
  public async Task ToAsyncEnumerable()
    => Assert.That(await GenerateInts(5)
                         .ToAsync()
                         .ToListAsync(),
                   Is.EqualTo(GenerateInts(5)));

  [Test]
  [TestCase(false)]
  [TestCase(true)]
  public async Task ToAsyncCancellation(bool enumeratorToken)
  {
    var cts = new CancellationTokenSource();

    await using var enumerator = enumeratorToken
                                   ? GenerateInts(5)
                                     .ToAsync()
                                     .GetAsyncEnumerator(cts.Token)
                                   : GenerateInts(5)
                                     .ToAsync(cts.Token)
                                     .GetAsyncEnumerator(CancellationToken.None);

    Assert.That(await enumerator.MoveNextAsync(),
                Is.True);
    Assert.That(enumerator.Current,
                Is.EqualTo(0));
    cts.Cancel();

    Assert.That(enumerator.MoveNextAsync,
                Throws.InstanceOf<OperationCanceledException>());
  }

  private static IEnumerable<int> GenerateInts(int n)
  {
    for (var i = 0; i < n; ++i)
    {
      yield return i;
    }
  }
}

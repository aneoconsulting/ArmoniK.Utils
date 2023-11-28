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

using NUnit.Framework;

namespace ArmoniK.Utils.Tests;

public class CollectionExtTest
{
  public enum OperationType
  {
    None,
    Iterate,
    Add,
    Remove,
  }

  public static readonly List<int>[] ListTestCases =
  {
    new(),
    new()
    {
      1,
    },
    new()
    {
      1,
      2,
    },
  };

  [Test]
  public void CollectionViewEnumeration([ValueSource(nameof(ListTestCases))] List<int>     collection,
                                        [Values]                             bool          isCollection,
                                        [Values]                             OperationType op)
  {
    var view = CreateView(collection,
                          isCollection,
                          Proj);

    AlterCollection(collection,
                    view,
                    op);

    Assert.That(view,
                Is.EqualTo(collection.Select(Proj)));
  }

  [Test]
  public void CollectionViewContains([ValueSource(nameof(ListTestCases))] List<int>     collection,
                                     [Values]                             bool          isCollection,
                                     [Values]                             OperationType op)
  {
    var view = CreateView(collection,
                          isCollection,
                          Proj);

    AlterCollection(collection,
                    view,
                    op);

    foreach (var x in collection.Select(Proj))
    {
      Assert.That(view,
                  Does.Contain(x));
    }
  }

  [Test]
  public void CollectionViewCount([ValueSource(nameof(ListTestCases))] List<int>     collection,
                                  [Values]                             bool          isCollection,
                                  [Values]                             OperationType op)
  {
    var view = CreateView(collection,
                          isCollection,
                          Proj);

    AlterCollection(collection,
                    view,
                    op);

    Assert.That(view,
                Has.Count.EqualTo(collection.Count));
  }

  [Test]
  public void CollectionViewCopyTo([ValueSource(nameof(ListTestCases))] List<int>     collection,
                                   [Values]                             bool          isCollection,
                                   [Values]                             OperationType op)
  {
    var view = CreateView(collection,
                          isCollection,
                          Proj);

    AlterCollection(collection,
                    view,
                    op);

    var buffer = new double[collection.Count + 1];
    buffer[0] = 0.0;
    view.CopyTo(buffer,
                1);

    collection.Insert(0,
                      0);

    Assert.That(buffer,
                Is.EqualTo(collection.Select(Proj)));
  }

  [Test]
  public void ListViewSubscript([ValueSource(nameof(ListTestCases))] List<int>     collection,
                                [Values]                             OperationType op)
  {
    var view = collection.ViewSelect(Proj);

    AlterCollection(collection,
                    view,
                    op);

    for (var i = 0; i < collection.Count; ++i)
    {
      Assert.That(view[i],
                  Is.EqualTo(Proj(collection[i])));
    }
  }

  [Test]
  public void ListViewIndexOf([ValueSource(nameof(ListTestCases))] List<int>     collection,
                              [Values]                             OperationType op)
  {
    var view = collection.ViewSelect(Proj);

    AlterCollection(collection,
                    view,
                    op);

    foreach (var x in collection)
    {
      Assert.That(view.IndexOf(Proj(x)),
                  Is.EqualTo(collection.IndexOf(x)));
    }

    Assert.That(view.IndexOf(0.0),
                Is.EqualTo(collection.IndexOf(0)));
  }

  [Test]
  public void CollectionViewUnsupportedMutability([Values] bool isCollection)
  {
    var view = CreateView(Array.Empty<int>(),
                          isCollection,
                          Proj);

    Assert.That(() => view.Add(0.0),
                Throws.InstanceOf<NotSupportedException>());
    Assert.That(() => view.Clear(),
                Throws.InstanceOf<NotSupportedException>());
    Assert.That(() => view.Remove(0.0),
                Throws.InstanceOf<NotSupportedException>());
  }

  [Test]
  public void ListViewUnsupportedMutability()
  {
    var view = Array.Empty<int>()
                    .ViewSelect(Proj);

    Assert.That(() => view.Insert(0,
                                  0.0),
                Throws.InstanceOf<NotSupportedException>());
    Assert.That(() => view.RemoveAt(0),
                Throws.InstanceOf<NotSupportedException>());
  }

  private static ICollection<double> CreateView(IList<int>        list,
                                                bool              isCollection,
                                                Func<int, double> proj)
    => isCollection
         ? (list as ICollection<int>).ViewSelect(proj)
         : list.ViewSelect(proj);

  private static void AlterCollection(ICollection<int>    list,
                                      IEnumerable<double> view,
                                      OperationType       op)
  {
    switch (op)
    {
      case OperationType.None:
        break;
      case OperationType.Iterate:
        _ = view.Sum();
        break;
      case OperationType.Add:
        list.Add(5);
        break;
      case OperationType.Remove:
      default:
        list.Remove(1);
        break;
    }
  }

  private static double Proj(int x)
    => -x;
}

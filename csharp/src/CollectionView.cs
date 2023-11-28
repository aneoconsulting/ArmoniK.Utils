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
using System.Collections.Generic;
using System.Linq;

using JetBrains.Annotations;

namespace ArmoniK.Utils;

/// <summary>
///   View over a collection of <typeparamref name="Tsrc" />.
///   All elements are projected into <typeparamref name="Tdst" />.
/// </summary>
/// <typeparam name="Tsrc">Type of the underlying collection elements</typeparam>
/// <typeparam name="Tdst">Type of the projected elements</typeparam>
/// <remarks>
///   If the underlying is modified, the view will reflect the changes.
///   If it is safe to iterate over the underlying collection while modifying it,
///   it is safe to iterate over the view while modifying the underlying collection.
/// </remarks>
[PublicAPI]
public class CollectionView<Tsrc, Tdst> : ICollection<Tdst>
{
  private readonly ICollection<Tsrc> collection_;
  private readonly Func<Tsrc, Tdst>  projection_;

  /// <summary>
  ///   Project <paramref name="collection" /> into a view
  /// </summary>
  /// <param name="collection">Collection to project</param>
  /// <param name="projection">
  ///   Function to project <typeparamref name="Tsrc" /> elements into <typeparamref name="Tdst" />
  ///   elements
  /// </param>
  [PublicAPI]
  public CollectionView(ICollection<Tsrc> collection,
                        Func<Tsrc, Tdst>  projection)
  {
    projection_ = projection;
    collection_ = collection;
  }

  /// <inheritdoc />
  public IEnumerator<Tdst> GetEnumerator()
    => collection_.Select(projection_)
                  .GetEnumerator();

  /// <inheritdoc />
  IEnumerator IEnumerable.GetEnumerator()
    => GetEnumerator();

  /// <inheritdoc />
  /// <remarks>
  ///   Operation is not supported on a view
  /// </remarks>
  public void Add(Tdst item)
    => throw new NotSupportedException("View Collection is read-only");

  /// <inheritdoc />
  /// <remarks>
  ///   Operation is not supported on a view
  /// </remarks>
  public void Clear()
    => throw new NotSupportedException("View Collection is read-only");

  /// <inheritdoc />
  /// <remarks>
  ///   The underlying collection should be entirely iterated over to check if <paramref name="item" /> is in the view
  /// </remarks>
  public bool Contains(Tdst item)
    => (this as IEnumerable<Tdst>).Contains(item);

  /// <inheritdoc />
  public void CopyTo(Tdst[] array,
                     int    arrayIndex)
  {
    foreach (var x in this)
    {
      array[arrayIndex++] = x;
    }
  }

  /// <inheritdoc />
  /// <remarks>
  ///   Operation is not supported on a view
  /// </remarks>
  public bool Remove(Tdst item)
    => throw new NotSupportedException("View Collection is read-only");

  /// <inheritdoc />
  public int Count
    => collection_.Count;

  /// <inheritdoc />
  /// <remarks>
  ///   View is always read only
  /// </remarks>
  public bool IsReadOnly
    => true;
}

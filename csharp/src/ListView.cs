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
///   View over a list of <typeparamref name="Tsrc" />.
///   All elements are projected into <typeparamref name="Tdst" />.
/// </summary>
/// <typeparam name="Tsrc">Type of the underlying list elements</typeparam>
/// <typeparam name="Tdst">Type of the projected elements</typeparam>
/// <remarks>
///   If the underlying list is modified, the view will reflect the changes.
///   If it is safe to iterate over the underlying list while modifying it,
///   it is safe to iterate over the view while modifying the underlying list.
/// </remarks>
/// <remarks>
///   The projection should be pure (no side effect).
/// </remarks>
[PublicAPI]
public readonly struct ListView<Tsrc, Tdst> : IList<Tdst>
{
  private readonly IList<Tsrc>      list_;
  private readonly Func<Tsrc, Tdst> projection_;

  /// <summary>
  ///   Project <paramref name="list" /> into a view
  /// </summary>
  /// <param name="list">List to project</param>
  /// <param name="projection">
  ///   Function to project <typeparamref name="Tsrc" /> elements into <typeparamref name="Tdst" />
  ///   elements
  /// </param>
  [PublicAPI]
  public ListView(IList<Tsrc>      list,
                  Func<Tsrc, Tdst> projection)
  {
    projection_ = projection;
    list_       = list;
  }

  /// <inheritdoc />
  public IEnumerator<Tdst> GetEnumerator()
    => list_.Select(projection_)
            .GetEnumerator();

  /// <inheritdoc />
  IEnumerator IEnumerable.GetEnumerator()
    => GetEnumerator();

  /// <inheritdoc />
  /// <remarks>
  ///   <see cref="Add" /> is not supported on a view
  /// </remarks>
  public void Add(Tdst item)
    => throw new NotSupportedException("View List is read-only");

  /// <inheritdoc />
  /// <remarks>
  ///   <see cref="Clear" /> is not supported on a view
  /// </remarks>
  public void Clear()
    => throw new NotSupportedException("View List is read-only");

  /// <inheritdoc />
  /// <remarks>
  ///   The underlying list should be entirely iterated over to check if <paramref name="item" /> is in the view,
  ///   even if the underlying list provides a fast way to check if it contains a given value.
  /// </remarks>
  public bool Contains(Tdst item)
  {
    var cmp = Comparer<Tdst>.Default;
    // ReSharper disable once LoopCanBeConvertedToQuery
    foreach (var x in this)
    {
      if (cmp.Compare(x,
                      item) == 0)
      {
        return true;
      }
    }

    return false;
  }

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
  ///   <see cref="Remove" /> is not supported on a view
  /// </remarks>
  public bool Remove(Tdst item)
    => throw new NotSupportedException("View List is read-only");

  /// <inheritdoc />
  public int Count
    => list_.Count;

  /// <inheritdoc />
  /// <remarks>
  ///   View is always read only
  /// </remarks>
  public bool IsReadOnly
    => true;

  /// <inheritdoc />
  public int IndexOf(Tdst item)
  {
    var cmp = Comparer<Tdst>.Default;
    var i   = 0;

    foreach (var x in this)
    {
      if (cmp.Compare(x,
                      item) == 0)
      {
        return i;
      }

      ++i;
    }

    return -1;
  }

  /// <inheritdoc />
  /// <remarks>
  ///   <see cref="Insert" /> is not supported on a view
  /// </remarks>
  public void Insert(int  index,
                     Tdst item)
    => throw new NotSupportedException("View List is read-only");

  /// <inheritdoc />
  /// <remarks>
  ///   <see cref="RemoveAt" /> is not supported on a view
  /// </remarks>
  public void RemoveAt(int index)
    => throw new NotSupportedException("View List is read-only");

  /// <inheritdoc />
  /// <remarks>
  ///   Set is not supported on a view
  /// </remarks>
  public Tdst this[int index]
  {
    get => projection_(list_[index]);
    set => throw new NotSupportedException("View List is read-only");
  }
}

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

using JetBrains.Annotations;

namespace ArmoniK.Utils;

/// <summary>
///   Extension class for <see cref="ICollection{T}" /> and <see cref="IList{T}" />
/// </summary>
public static class CollectionExt
{
  /// <summary>
  ///   Project <paramref name="collection" /> into a view.
  /// </summary>
  /// <param name="collection">Collection to project</param>
  /// <param name="projection">
  ///   Function to project <typeparamref name="Tsrc" /> elements into <typeparamref name="Tdst" />
  ///   elements
  /// </param>
  /// <typeparam name="Tsrc">Type of the underlying collection elements</typeparam>
  /// <typeparam name="Tdst">Type of the projected elements</typeparam>
  /// <returns>
  ///   Projected view over the <paramref name="collection" />.
  /// </returns>
  /// <remarks>
  ///   <paramref name="projection" /> should be pure (no side effect).
  /// </remarks>
  [PublicAPI]
  public static CollectionView<Tsrc, Tdst> ViewSelect<Tsrc, Tdst>(this ICollection<Tsrc> collection,
                                                                  Func<Tsrc, Tdst>       projection)
    => new(collection,
           projection);

  /// <summary>
  ///   Project <paramref name="list" /> into a view.
  /// </summary>
  /// <param name="list">List to project</param>
  /// <param name="projection">
  ///   Function to project <typeparamref name="Tsrc" /> elements into <typeparamref name="Tdst" />
  ///   elements
  /// </param>
  /// <typeparam name="Tsrc">Type of the underlying list elements</typeparam>
  /// <typeparam name="Tdst">Type of the projected elements</typeparam>
  /// <returns>
  ///   Projected view over the <paramref name="list" />.
  /// </returns>
  /// <remarks>
  ///   <paramref name="projection" /> should be pure (no side effect).
  /// </remarks>
  [PublicAPI]
  public static ListView<Tsrc, Tdst> ViewSelect<Tsrc, Tdst>(this IList<Tsrc> list,
                                                            Func<Tsrc, Tdst> projection)
    => new(list,
           projection);
}

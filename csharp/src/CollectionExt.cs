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

using JetBrains.Annotations;

namespace ArmoniK.Utils;

public static class CollectionExt
{
  /// <summary>
  ///   Creates a view over a collection
  /// </summary>
  /// <param name="collection"></param>
  /// <param name="projection"></param>
  /// <typeparam name="Tsrc"></typeparam>
  /// <typeparam name="Tdst"></typeparam>
  /// <returns></returns>
  [PublicAPI]
  public static CollectionView<Tsrc, Tdst> ViewSelect<Tsrc, Tdst>(this ICollection<Tsrc> collection,
                                                                  Func<Tsrc, Tdst>       projection)
    => new(collection,
           projection);
}

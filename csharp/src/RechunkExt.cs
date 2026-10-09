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

using JetBrains.Annotations;

namespace ArmoniK.Utils;

/// <summary>
///   Extension class to rechunk sequences of memory chunks
/// </summary>
public static class RechunkExt
{
  /// <summary>
  ///   Rechunk a sequence of memory chunks so that every chunk, except the last one,
  ///   has a size between <paramref name="chunkMinSize" /> and <paramref name="chunkMaxSize" />.
  /// </summary>
  /// <remarks>
  ///   <para>
  ///     Consecutive input chunks are accumulated until at least <paramref name="chunkMinSize" /> elements are
  ///     available, and input chunks larger than <paramref name="chunkMaxSize" /> are split.
  ///     The concatenation of the output chunks is equal to the concatenation of the input chunks.
  ///     Empty input chunks are ignored, and no empty chunk is ever yielded.
  ///   </para>
  ///   <para>
  ///     Copies are avoided whenever possible: input chunks within bounds are yielded as is, and oversized input
  ///     chunks are sliced. Only fragments smaller than <paramref name="chunkMinSize" /> are copied into a newly
  ///     allocated array in order to be merged with the following input chunks.
  ///     Therefore, a yielded chunk is either backed by memory owned by the source, and has the same lifetime as
  ///     the input chunks, or backed by an array owned by the caller.
  ///   </para>
  ///   <para>
  ///     The source is free to reuse the memory of an input chunk once its next element has been requested.
  ///   </para>
  /// </remarks>
  /// <param name="source">Input chunks</param>
  /// <param name="chunkMinSize">Minimum size of the output chunks, except the last one</param>
  /// <param name="chunkMaxSize">Maximum size of the output chunks</param>
  /// <typeparam name="T">Type of the elements</typeparam>
  /// <returns>
  ///   An <see cref="IAsyncEnumerable{T}" /> with the same elements as <paramref name="source" />, rechunked.
  /// </returns>
  /// <exception cref="ArgumentOutOfRangeException">
  ///   <paramref name="chunkMinSize" /> is below 1, or <paramref name="chunkMaxSize" /> is below
  ///   <paramref name="chunkMinSize" />.
  /// </exception>
  [PublicAPI]
  public static IAsyncEnumerable<ReadOnlyMemory<T>> Rechunk<T>(this IAsyncEnumerable<ReadOnlyMemory<T>>? source,
                                                               int                                       chunkMinSize,
                                                               int                                       chunkMaxSize)
  {
    if (chunkMinSize < 1)
    {
      throw new ArgumentOutOfRangeException(nameof(chunkMinSize),
                                            chunkMinSize,
                                            "Minimum chunk size must be at least 1");
    }

    if (chunkMaxSize < chunkMinSize)
    {
      throw new ArgumentOutOfRangeException(nameof(chunkMaxSize),
                                            chunkMaxSize,
                                            "Maximum chunk size must be at least the minimum chunk size");
    }

    if (source is null)
    {
      return AsyncEnumerable.Empty<ReadOnlyMemory<T>>();
    }

    return new Rechunker<T>(source,
                            chunkMinSize,
                            chunkMaxSize);
  }
}

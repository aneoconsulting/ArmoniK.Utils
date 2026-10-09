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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace ArmoniK.Utils;

internal static class Rechunker
{
  // Implementation of the Rechunk function
  // Input chunks within bounds are yielded as is, and oversized input chunks are sliced, without any copy.
  // Only fragments smaller than minSize are copied into a buffer, in order to be merged with the next input chunks.
  // No reference to an input chunk is kept across a MoveNextAsync of the source: the source can reuse its memory.
  internal static async IAsyncEnumerable<ReadOnlyMemory<T>> IteratorAsync<T>(IAsyncEnumerable<ReadOnlyMemory<T>>        source,
                                                                             int                                        minSize,
                                                                             int                                        maxSize,
                                                                             [EnumeratorCancellation] CancellationToken cancellationToken = default)
  {
    // Accumulation buffer of size minSize, allocated lazily.
    // Only the first count elements are valid, and count is always below minSize between two input chunks.
    T[]? buffer = null;
    var  count  = 0;

    cancellationToken.ThrowIfCancellationRequested();

    await foreach (var chunk in source.WithCancellation(cancellationToken)
                                      .ConfigureAwait(false))
    {
      var pending = chunk;

      // Buffer is not empty: complete it
      if (count > 0)
      {
        var missing = minSize - count;
        if (pending.Length < missing)
        {
          pending.Span.CopyTo(buffer.AsSpan(count));
          count += pending.Length;
          continue;
        }

        // If what remains after reaching minSize cannot be yielded on its own, it would have to be copied anyway:
        // absorb as much as possible into this chunk instead
        var take = pending.Length - missing >= minSize
                     ? missing
                     : Math.Min(pending.Length,
                                maxSize - count);

        T[] merged;
        if (take == missing)
        {
          // Buffer becomes exactly full: yield it as is, and allocate a fresh one for the next chunk
          merged = buffer!;
          buffer = null;
        }
        else
        {
          // Chunk is larger than the buffer: allocate it with its exact size, and keep the buffer for later
          merged = new T[count + take];
          buffer.AsSpan(0,
                        count)
                .CopyTo(merged);
        }

        pending.Span.Slice(0,
                           take)
               .CopyTo(merged.AsSpan(count));
        pending = pending.Slice(take);
        count   = 0;

        yield return merged;
        cancellationToken.ThrowIfCancellationRequested();
      }

      // Buffer is empty: slice what is large enough without copy
      while (pending.Length >= minSize)
      {
        var size = SliceSize(pending.Length,
                             minSize,
                             maxSize);
        var slice = pending.Slice(0,
                                  size);
        pending = pending.Slice(size);

        yield return slice;
        cancellationToken.ThrowIfCancellationRequested();
      }

      // Fragment too small: it must be merged with the next input chunks
      if (!pending.IsEmpty)
      {
        buffer ??= new T[minSize];
        pending.Span.CopyTo(buffer);
        count = pending.Length;
      }
    }

    // Last chunk can be smaller than minSize
    if (count > 0)
    {
      yield return new ReadOnlyMemory<T>(buffer,
                                         0,
                                         count);
    }
  }

  // Size of the next slice of a chunk of the given length, with length >= minSize
  private static int SliceSize(int length,
                               int minSize,
                               int maxSize)
  {
    if (length <= maxSize)
    {
      return length;
    }

    if (length - maxSize >= minSize)
    {
      return maxSize;
    }

    if (length - minSize >= minSize)
    {
      // Leave exactly minSize elements so that the remainder can also be yielded without copy
      return length - minSize;
    }

    // Cannot be split in two valid chunks: minimize the remainder that will be copied
    return maxSize;
  }
}

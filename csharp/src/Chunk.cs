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
using System.Runtime.InteropServices;
using System.Threading;

namespace ArmoniK.Utils;

internal static class Chunk
{
  // Implementation of the AsChunked function
  // Original source code : https://github.com/dotnet/runtime/blob/main/src/libraries/System.Linq/src/System/Linq/Chunk.cs
  internal static IEnumerable<TSource[]> Iterator<TSource>(IEnumerable<TSource> source,
                                                           int                  size)
  {
    using var e = source.GetEnumerator();

    var buffer = Array.Empty<TSource>();
    int bufferSize;

    {
      // first chunk
      for (bufferSize = 0; bufferSize < size && e.MoveNext(); ++bufferSize)
      {
        if (bufferSize >= buffer.Length)
        {
          var newLength = Math.Min(Math.Max(buffer.Length + buffer.Length / 2,
                                            4),
                                   size);
          Array.Resize(ref buffer,
                       newLength);
        }

        buffer[bufferSize] = e.Current;
      }
    }
    // buffer is now the right size here

    while (true) // other chunks
    {
      if (bufferSize != size) // Incomplete chunk
      {
        // chunk is not empty, and must be trimmed and return
        if (bufferSize > 0)
        {
          Array.Resize(ref buffer,
                       bufferSize);
          yield return buffer;
        }

        yield break;
      }

      yield return buffer; // chunk is complete and a new storage is required
      buffer = new TSource[size];

      for (bufferSize = 0; bufferSize < size && e.MoveNext(); ++bufferSize)
      {
        buffer[bufferSize] = e.Current;
      }
    }
  }


  // Implementation of the ToChunksAsync function, on top of the rechunker
  internal static IAsyncEnumerable<T[]> IteratorAsync<T>(IAsyncEnumerable<T> enumerable,
                                                         int                 size,
                                                         TimeSpan            maxDelay,
                                                         ChunkFlusher?       flusher,
                                                         CancellationToken   cancellationToken)
    => Rechunker.IteratorAsync<T, T, T[], ElementAdapter<T>>(enumerable,
                                                             default,
                                                             size,
                                                             size,
                                                             maxDelay,
                                                             flusher,
                                                             cancellationToken);

  // Present each element to the rechunker as a single element chunk, backed by an array reused for all the elements.
  // This is safe because the rechunker never keeps an input chunk across a MoveNextAsync of the source.
  // The array is allocated lazily, so that each enumeration (which works on its own copy of the adapter) has its own.
  private struct ElementAdapter<T> : Rechunker.IAdapter<T, T, T[]>
  {
    private T[]? box_;

    public ReadOnlyMemory<T> ToMemory(T item)
    {
      box_    ??= new T[1];
      box_[0] =   item;
      return box_;
    }

    // Arrays from the rechunker buffer have the exact chunk size and can be returned as is.
    // Anything else (only the reused array, when size is 1) must be copied.
    public T[] FromMemory(ReadOnlyMemory<T> chunk)
      => MemoryMarshal.TryGetArray(chunk,
                                   out var segment)                           && !ReferenceEquals(segment.Array,
                                                                                                  box_) && segment.Offset == 0 && segment.Count == segment.Array!.Length
           ? segment.Array
           : chunk.ToArray();
  }
}

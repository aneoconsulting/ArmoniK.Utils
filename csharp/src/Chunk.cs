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


  // Implementation of the ToChunksAsync function
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

  // Each element is given as a chunk backed by the same array: safe as the rechunker never keeps an input chunk
  // across a MoveNextAsync. Allocated lazily so that each enumeration (with its own adapter copy) has its own.
  private struct ElementAdapter<T> : Rechunker.IAdapter<T, T, T[]>
  {
    private T[]? box_;

    public ReadOnlyMemory<T> ToMemory(T item)
    {
      box_    ??= new T[1];
      box_[0] =   item;
      return box_;
    }

    // Arrays built by the rechunker can be returned as is, but not the shared one (only yielded when size is 1)
    public T[] FromMemory(ReadOnlyMemory<T> chunk)
    {
      if (MemoryMarshal.TryGetArray(chunk,
                                    out var segment) && segment.Array != box_ && segment.Count == segment.Array!.Length)
      {
        return segment.Array;
      }

      return chunk.ToArray();
    }
  }
}

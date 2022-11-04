// This file is part of the ArmoniK project
//
// Copyright (C) ANEO, 2022-2022.All rights reserved.
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

namespace ArmoniK.Utils;

internal static class Chunk
{
  private struct ArrayGrower<T>
  {
    private T[] array_;
    private int count_;
    private readonly int maxSize_;
    public ArrayGrower(int maxSize, int? initialCapacity = null)
    {
      maxSize_ = maxSize;
      count_ = 0;
      array_ = initialCapacity is null ? Array.Empty<T>() : new T[initialCapacity.Value];
    }
    internal T[] InternalArray => array_;
    public int Count => count_;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AddAndGrow(T element)
    {
      var length = array_.Length;
      if (length != maxSize_ && count_ == length)
      {
        var newLength = Math.Min(Math.Max(length + length /2, 4), maxSize_);
        Array.Resize(ref array_, newLength);
      }
      array_[count_++] = element; // Will intentionally throw if more elements than size are added
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(T element)
    {
      array_[count_++] = element; // Will intentionally throw if more elements than size are added
    }
    public void Clear()
    {
      count_ = 0;
    }
    public void Trimm()
    {
      if (count_ != array_.Length)
      {
        Array.Resize(ref array_, count_);
      }
    }
  }
  // Implementation of the AsChunked function
  // Original source code : https://github.com/dotnet/runtime/blob/main/src/libraries/System.Linq/src/System/Linq/Chunk.cs
  internal static IEnumerable<TSource[]> Iterator<TSource>(IEnumerable<TSource> source,
                                                            int size)
  {
    using var e = source.GetEnumerator();

    var buffer = new ArrayGrower<TSource>(size); // cheap allocation can be afforded

    { // first chunk
      for (var i = 0; i < size && e.MoveNext(); ++i)
      {
        buffer.AddAndGrow(e.Current);
      }
    }
    // buffer is now the right size here

    while (true) // other chunks
    {
      if (buffer.Count != size) // Incomplete chunk
      {
        // chunk is not empty, and must be trimmed and return
        if (buffer.Count > 0)
        {
          buffer.Trimm();
          yield return buffer.InternalArray;
        }
        yield break;
      }
      yield return buffer.InternalArray; // chunk is complete and a new storage is required

      buffer.Clear();
      for (var i = 0; i < size && e.MoveNext(); ++i)
      {
        buffer.Add(e.Current);
      }
    }
  }
}

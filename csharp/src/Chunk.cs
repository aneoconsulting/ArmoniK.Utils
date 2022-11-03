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

using System.Diagnostics;

namespace ArmoniK.Utils;

internal static class Chunk
{
  // Implementation of the AsChunked function
  // Original source code : https://github.com/dotnet/runtime/blob/main/src/libraries/System.Linq/src/System/Linq/Chunk.cs
  internal static IEnumerable<TSource[]> Iterator<TSource>(IEnumerable<TSource> source,
                                                           int                  size)
  {
    using var e = source.GetEnumerator();

    // Before allocating anything, make sure there's at least one element.
    if (e.MoveNext())
    {
      // Now that we know we have at least one item, allocate an initial storage array. This is not
      // the array we'll yield.  It starts out small in order to avoid significantly overallocating
      // when the source has many fewer elements than the chunk size.
      var arraySize = Math.Min(size,
                               4);
      int i;
      do
      {
        var array = new TSource[arraySize];

        // Store the first item.
        array[0] = e.Current;
        i        = 1;

        if (size != array.Length)
        {
          // This is the first chunk. As we fill the array, grow it as needed.
          for (; i < size && e.MoveNext(); i++)
          {
            if (i >= array.Length)
            {
              arraySize = (int)Math.Min((uint)size,
                                        2 * (uint)array.Length);
              Array.Resize(ref array,
                           arraySize);
            }

            array[i] = e.Current;
          }
        }
        else
        {
          // For all but the first chunk, the array will already be correctly sized.
          // We can just store into it until either it's full or MoveNext returns false.
          var local = array; // avoid bounds checks by using cached local (`array` is lifted to iterator object as a field)
          Debug.Assert(local.Length == size);
          for (; (uint)i < (uint)local.Length && e.MoveNext(); i++)
          {
            local[i] = e.Current;
          }
        }

        if (i != array.Length)
        {
          Array.Resize(ref array,
                       i);
        }

        yield return array;
      } while (i >= size && e.MoveNext());
    }
  }
}

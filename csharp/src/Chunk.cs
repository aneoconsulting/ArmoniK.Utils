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

namespace ArmoniK.Utils;

internal static class Chunk
{
  // Implementation of the AsChunked function
  // Original source code : https://github.com/dotnet/runtime/blob/main/src/libraries/System.Linq/src/System/Linq/Chunk.cs
  internal static IEnumerable<TSource[]> Iterator<TSource>(IEnumerable<TSource> source,
                                                           int                  size)
  {
    using var e = source.GetEnumerator();

    // TODO: defer allocation until we are sure to have at least one element
    var buffer = new TSource[size];

    // Bad, I know
    while (true)
    {
      var i = 0;
      do
      {
        // This is the exit point
        if (!e.MoveNext())
        {
          if (i > 0)
          {
            Array.Resize(ref buffer,
                         i);
            yield return buffer;
          }

          yield break;
        }

        buffer[i] =  e.Current;
        i         += 1;
      } while (i < size);

      yield return buffer;
    }
  }
}

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
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace ArmoniK.Utils;

public static class ChunkAsync
{
  public static async IAsyncEnumerable<T[]> AsChunksAsync<T>(this IAsyncEnumerable<T>                   enumerable,
                                                             int                                        chunkSize,
                                                             TimeSpan                                   delay,
                                                             [EnumeratorCancellation] CancellationToken cancellationToken)
  {
    await using var enumerator = enumerable.GetAsyncEnumerator(cancellationToken);
    var             chunk      = new List<T>();

    Task? timeoutTask = null;
    var nextTask = enumerator.MoveNextAsync()
                             .AsTask();

    Exception? error = null;

    while (true)
    {
      Task which;
      if (timeoutTask is null)
      {
        which = nextTask;
      }
      else
      {
        which = await Task.WhenAny(nextTask,
                                   timeoutTask)
                          .ConfigureAwait(false);
      }

      try
      {
        await which.ConfigureAwait(false);
      }
      catch (Exception e)
      {
        error = e;
        break;
      }

      if (error is not null)
      {
        break;
      }

      if (ReferenceEquals(which,
                          nextTask))
      {
        if (!await nextTask.ConfigureAwait(false))
        {
          break;
        }

        chunk.Add(enumerator.Current);
        if (chunk.Count >= chunkSize)
        {
          yield return chunk.ToArray();
          chunk = new List<T>();

          timeoutTask?.Dispose();
          timeoutTask = null;
        }
        else if (timeoutTask is null)
        {
          timeoutTask = Task.Delay(delay,
                                   cancellationToken);
        }

        nextTask = enumerator.MoveNextAsync()
                             .AsTask();
        continue;
      }

      yield return chunk.ToArray();
      chunk = new List<T>();

      timeoutTask?.Dispose();
      timeoutTask = null;
    }

    if (chunk.Any())
    {
      yield return chunk.ToArray();
    }

    timeoutTask?.Dispose();

    if (error is not null)
    {
      ExceptionDispatchInfo.Capture(error)
                           .Throw();
    }
  }
}
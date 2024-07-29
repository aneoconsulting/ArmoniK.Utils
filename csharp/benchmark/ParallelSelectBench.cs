// This file is part of the ArmoniK project
//
// Copyright (C) ANEO, 2022-2024.All rights reserved.
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
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;

namespace ArmoniK.Utils.Benchmark;

[MemoryDiagnoser]
public class ParallelSelectBench
{
  [ParamsSource(nameof(RangeSource))]
  public IEnumerable<int> Range = Enumerable.Empty<int>();

  [Benchmark]
  public async Task<int> SelectOrdered()
  {
    var s = 0;
    await foreach (var i in Range.ParallelSelect (new (2),
                                                  Task.FromResult))
    {
      s += i;
    }

    return s;
  }


  [Benchmark]
  public async Task<int> SelectUnordered()
  {
    var s = 0;
    await foreach (var i in Range.ParallelSelect (new (true, 2),
                                                  Task.FromResult))
    {
      s += i;
    }

    return s;
  }

  [Benchmark]
  public async Task<int> Foreach()
  {
    var s = 0;
    await Range.ParallelForEach(new(2),
                                i =>
                                {
                                  Interlocked.Add(ref s,
                                                  i);
                                  return Task.CompletedTask;
                                })
               .ConfigureAwait(false);

    return s;
  }

  [Benchmark]
  public Task<int> Pouet()
    => Range.ToAsyncEnumerable()
            .ParallelSelect(new ParallelTaskOptions(2), i => Task.FromResult(new int[i]))
            .ParallelSelect(new ParallelTaskOptions(2), a => Task.FromResult(a.Sum()))
            .SumAsync()
            .AsTask();

  [Benchmark]
  public Task<int> Pouet2()
    => Range.ToAsyncEnumerable()
            .Select(i => new int[i])
            .Select(a => a.Sum())
            .SumAsync()
            .AsTask();


  public static IEnumerable<IEnumerable<int>> RangeSource()
  {
    yield return Enumerable.Range(0,
                                  10000);
    yield return Enumerable.Range(0,
                                  10000).ToArray();
  }
}

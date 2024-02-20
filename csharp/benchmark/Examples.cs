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

using System.Collections;
using System.Collections.Generic;
using System.Linq;

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;

namespace ArmoniK.Utils.Benchmark;

[MemoryDiagnoser]
public class Examples
{
  [ParamsSource(nameof(RangeSource))]
  public IEnumerable<int> Range = Enumerable.Empty<int>();

  [Benchmark]
  public IList<int> AsIList()
    => Range.AsIList();

  [Benchmark]
  public ICollection<int> AsICollection()
    => Range.AsICollection();

  [Benchmark]
  public List<int> ToList()
    => Range.ToList();

  [Benchmark]
  public int[] ToArray()
    => Range.ToArray();

  public static void Main(string[] args)
    => BenchmarkSwitcher.FromTypes(new[]
                                   {
                                     typeof(Examples),
                                   })
                        .Run(args);

  public static IEnumerable<IEnumerable<int>> RangeSource()
  {
    yield return new RangeWrapper(1000);
    yield return Enumerable.Range(0,
                                  1000);
    yield return new int[1000];
  }

  private class RangeWrapper : IEnumerable<int>
  {
    private readonly IEnumerable<int> range_;

    public RangeWrapper(int n)
      => range_ = Enumerable.Range(0,
                                   n);


    public IEnumerator<int> GetEnumerator()
      => range_.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator()
      => GetEnumerator();
  }
}

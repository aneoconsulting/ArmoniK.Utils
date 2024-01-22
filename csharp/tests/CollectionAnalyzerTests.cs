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

using System.Threading.Tasks;

using ArmoniK.Utils.Diagnostics;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.Testing.Verifiers;

using NUnit.Framework;
#if NET47 || NET471 || NET472
using System.Reflection;
#endif

namespace ArmoniK.Utils.Tests;

public class CollectionAnalyzerTests
{
  [Test]
  public Task Enumerable_AsICollection()
  {
    const string text = "Enumerable.Empty<int>().AsICollection();";

    return AnalyzerTest(text);
  }

  [Test]
  public Task Enumerable_AsICollectionExt()
  {
    const string text = "EnumerableExt.AsICollection(Enumerable.Empty<int>());";

    return AnalyzerTest(text);
  }

  [Test]
  public Task Array_AsICollection()
  {
    const string text = "new int[]{}.AsICollection();";

    return AnalyzerTest(text);
  }

  [Test]
  public Task Array_AsICollectionExt()
  {
    const string text = "EnumerableExt.AsICollection(new int[]{});";

    return AnalyzerTest(text);
  }

  [Test]
  public Task Collection_AsICollection()
  {
    const string text = "new int[]{}.AsICollection().AsICollection();";

    var expected = new DiagnosticResult(CollectionAnalyzer.RedundantAsICollection).WithLocation(10,
                                                                                                1)
                                                                                  .WithArguments("new int[]{}.AsICollection()");

    return AnalyzerTest(text,
                        expected);
  }

  [Test]
  public Task Collection_AsICollectionExt()
  {
    const string text = "EnumerableExt.AsICollection(new int[]{}.AsICollection());";

    var expected = new DiagnosticResult(CollectionAnalyzer.RedundantAsICollection).WithLocation(10,
                                                                                                1)
                                                                                  .WithArguments("new int[]{}.AsICollection()");

    return AnalyzerTest(text,
                        expected);
  }

  [Test]
  public Task Enumerable_AsIList()
  {
    const string text = "Enumerable.Empty<int>().AsIList();";

    return AnalyzerTest(text);
  }

  [Test]
  public Task Enumerable_AsIListExt()
  {
    const string text = "EnumerableExt.AsIList(Enumerable.Empty<int>());";

    return AnalyzerTest(text);
  }

  [Test]
  public Task Array_AsIList()
  {
    const string text = "new int[]{}.AsIList();";

    return AnalyzerTest(text);
  }

  [Test]
  public Task Array_AsIListExt()
  {
    const string text = "EnumerableExt.AsIList(new int[]{});";

    return AnalyzerTest(text);
  }

  [Test]
  public Task List_AsIList()
  {
    const string text = "new int[]{}.AsIList().AsIList();";

    var expected = new DiagnosticResult(CollectionAnalyzer.RedundantAsIList).WithLocation(10,
                                                                                          1)
                                                                            .WithArguments("new int[]{}.AsIList()");

    return AnalyzerTest(text,
                        expected);
  }

  [Test]
  public Task List_AsIListExt()
  {
    const string text = "EnumerableExt.AsIList(new int[]{}.AsIList());";

    var expected = new DiagnosticResult(CollectionAnalyzer.RedundantAsIList).WithLocation(10,
                                                                                          1)
                                                                            .WithArguments("new int[]{}.AsIList()");

    return AnalyzerTest(text,
                        expected);
  }

  private Task AnalyzerTest(string                    sourceBlock,
                            params DiagnosticResult[] diagnostics)
  {
    var source = $@"
using System.Linq;
using System.Collections.Generic;
using ArmoniK.Utils;
#nullable enable
public static class Program
{{
public static void Main()
{{
{sourceBlock}
}}
}}
";

    var analyzerTest = new CSharpAnalyzerTest<CollectionAnalyzer, NUnitVerifier>
                       {
                         TestState =
                         {
                           Sources =
                           {
                             source,
                           },
                           AdditionalReferences =
                           {
#if NET47 || NET471 || NET472
                             MetadataReference.CreateFromFile(Assembly.Load("netstandard")
                                                                      .Location),
#endif
                             MetadataReference.CreateFromFile(typeof(EnumerableExt).Assembly.Location),
                           },
                         },
                       };
    analyzerTest.ExpectedDiagnostics.AddRange(diagnostics);

    return analyzerTest.RunAsync();
  }
}

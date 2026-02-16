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

using System.Threading.Tasks;

using ArmoniK.Utils.Diagnostics;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;

using NUnit.Framework;
#if NET47 || NET471 || NET472
using System.Reflection;
#endif

namespace ArmoniK.Utils.Tests;

public class CollectionCodeFixTests
{
  [Test]
  public Task Collection_AsICollection()
  {
    const string text      = "new int[]{}.AsICollection().AsICollection();";
    const string textFixed = "new int[]{}.AsICollection();";

    var expected = new DiagnosticResult(CollectionAnalyzer.RedundantAsICollection).WithLocation(10,
                                                                                                1)
                                                                                  .WithArguments("new int[]{}.AsICollection()");

    return CodeFixTest(text,
                       textFixed,
                       null,
                       expected);
  }

  [Test]
  public Task Collection_AsICollectionExt()
  {
    const string text      = "EnumerableExt.AsICollection(new int[]{}.AsICollection());";
    const string textFixed = "new int[]{}.AsICollection();";

    var expected = new DiagnosticResult(CollectionAnalyzer.RedundantAsICollection).WithLocation(10,
                                                                                                1)
                                                                                  .WithArguments("new int[]{}.AsICollection()");

    return CodeFixTest(text,
                       textFixed,
                       null,
                       expected);
  }

  [Test]
  public Task List_AsIList()
  {
    const string text      = "new int[]{}.AsIList().AsIList();";
    const string textFixed = "new int[]{}.AsIList();";

    var expected = new DiagnosticResult(CollectionAnalyzer.RedundantAsIList).WithLocation(10,
                                                                                          1)
                                                                            .WithArguments("new int[]{}.AsIList()");

    return CodeFixTest(text,
                       textFixed,
                       null,
                       expected);
  }

  [Test]
  public Task List_AsIListExt()
  {
    const string text      = "EnumerableExt.AsIList(new int[]{}.AsIList());";
    const string textFixed = "new int[]{}.AsIList();";

    var expected = new DiagnosticResult(CollectionAnalyzer.RedundantAsIList).WithLocation(10,
                                                                                          1)
                                                                            .WithArguments("new int[]{}.AsIList()");

    return CodeFixTest(text,
                       textFixed,
                       null,
                       expected);
  }

  [Test]
  public Task Combined()
  {
    const string text      = "new int[]{}.AsICollection().AsICollection();\nnew int[]{}.AsIList().AsIList();";
    const string textFixed = "new int[]{}.AsICollection();\nnew int[]{}.AsIList();";

    var expected1 = new DiagnosticResult(CollectionAnalyzer.RedundantAsICollection).WithLocation(10,
                                                                                                 1)
                                                                                   .WithArguments("new int[]{}.AsICollection()");
    var expected2 = new DiagnosticResult(CollectionAnalyzer.RedundantAsIList).WithLocation(11,
                                                                                           1)
                                                                             .WithArguments("new int[]{}.AsIList()");

    return CodeFixTest(text,
                       textFixed,
                       1,
                       expected1,
                       expected2);
  }

  [Test]
  public Task Collection_Nested()
  {
    const string text      = "(new int[]{}.AsICollection().AsICollection()).AsICollection();";
    const string textFixed = "new int[]{}.AsICollection();";

    var expected1 = new DiagnosticResult(CollectionAnalyzer.RedundantAsICollection).WithLocation(10,
                                                                                                 2)
                                                                                   .WithArguments("new int[]{}.AsICollection()");
    var expected2 = new DiagnosticResult(CollectionAnalyzer.RedundantAsICollection).WithLocation(10,
                                                                                                 1)
                                                                                   .WithArguments("(new int[]{}.AsICollection().AsICollection()=");

    return CodeFixTest(text,
                       textFixed,
                       2,
                       expected1,
                       expected2);
  }

  [Test]
  public Task List_Nested()
  {
    const string text      = "(new int[]{}.AsIList().AsIList()).AsIList();";
    const string textFixed = "new int[]{}.AsIList();";

    var expected1 = new DiagnosticResult(CollectionAnalyzer.RedundantAsIList).WithLocation(10,
                                                                                           2)
                                                                             .WithArguments("new int[]{}.AsIList()");
    var expected2 = new DiagnosticResult(CollectionAnalyzer.RedundantAsIList).WithLocation(10,
                                                                                           1)
                                                                             .WithArguments("(new int[]{}.AsIList().AsIList())");

    return CodeFixTest(text,
                       textFixed,
                       2,
                       expected1,
                       expected2);
  }

  private Task CodeFixTest(string                    sourceBlock,
                           string                    sourceBlockFixed,
                           int?                      iterations = null,
                           params DiagnosticResult[] diagnostics)
  {
    const string sourceHeader = @"
using System.Linq;
using System.Collections.Generic;
using ArmoniK.Utils;
#nullable enable
public static class Program
{
public static void Main()
{
";
    const string sourceFooter = @"
}
}
";
    var source      = $"{sourceHeader}{sourceBlock}{sourceFooter}";
    var sourceFixed = $"{sourceHeader}{sourceBlockFixed}{sourceFooter}";

    var codeFixTest = new CSharpCodeFixTest<CollectionAnalyzer, CollectionCodeFixProvider, DefaultVerifier>
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
                        FixedCode                = sourceFixed,
                        NumberOfFixAllIterations = iterations,
                      };
    codeFixTest.ExpectedDiagnostics.AddRange(diagnostics);

    return codeFixTest.RunAsync();
  }
}

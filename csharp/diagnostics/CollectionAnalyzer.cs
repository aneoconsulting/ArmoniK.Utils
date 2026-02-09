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

using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace ArmoniK.Utils.Diagnostics;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class CollectionAnalyzer : DiagnosticAnalyzer
{
  public static readonly DiagnosticDescriptor RedundantAsICollection = new("AKU0001",
                                                                           "Redundant AsICollection",
                                                                           "Enumerable is already a ICollection",
                                                                           "Usage",
                                                                           DiagnosticSeverity.Warning,
                                                                           true,
                                                                           "It is unnecessary to call AsICollection on an object of type ICollection.\nEnumerable '{0}' is already a ICollection.");

  public static readonly DiagnosticDescriptor RedundantAsIList = new("AKU0002",
                                                                     "Redundant AsIList",
                                                                     "Enumerable is already a IList",
                                                                     "Usage",
                                                                     DiagnosticSeverity.Warning,
                                                                     true,
                                                                     "It is unnecessary to call AsIList on an object of type IList.\nEnumerable '{0}' is already a IList.");

  public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
    => ImmutableArray.Create(RedundantAsICollection,
                             RedundantAsIList);

  public override void Initialize(AnalysisContext context)
  {
    // You must call this method to avoid analyzing generated code.
    context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

    // You must call this method to enable the Concurrent Execution.
    context.EnableConcurrentExecution();

    // Subscribe to semantic (compile time) action invocation, e.g. method invocation.
    context.RegisterOperationAction(AnalyzeInvocation,
                                    OperationKind.Invocation);
  }


  private void AnalyzeInvocation(OperationAnalysisContext context)
  {
    if (context.Operation is not IInvocationOperation invocationOperation || context.Operation.Syntax is not InvocationExpressionSyntax invocationSyntax)
    {
      return;
    }


    var methodSymbol = invocationOperation.TargetMethod;

    if (!methodSymbol.IsExtensionMethod || !Helpers.CheckNamespace(methodSymbol.ContainingType,
                                                                   "ArmoniK",
                                                                   "Utils",
                                                                   "EnumerableExt"))
    {
      return;
    }


    var instance = invocationOperation.Arguments[0]
                                      .ChildOperations.First();

    if (instance.Kind is OperationKind.Conversion && instance.IsImplicit)
    {
      instance = instance.ChildOperations.First();
    }

    var instanceAssembly = instance.Type?.ContainingAssembly?.Name;
    var instanceIsSystem = instanceAssembly is "mscorlib" or "System.Runtime";

    if (instance.ConstantValue is
        {
          HasValue: true,
          Value   : null,
        })
    {
      context.ReportDiagnostic(Diagnostic.Create(RedundantAsIList,
                                                 context.Operation.Syntax.GetLocation(),
                                                 "null"));
    }

    switch (methodSymbol.Name)
    {
      case "AsIList":
        if (instanceIsSystem && Helpers.CheckNamespace(instance.Type,
                                                       "System",
                                                       "Collections",
                                                       "Generic",
                                                       "IList") && instance.Type?.NullableAnnotation is NullableAnnotation.NotAnnotated)
        {
          context.ReportDiagnostic(Diagnostic.Create(RedundantAsIList,
                                                     context.Operation.Syntax.GetLocation(),
                                                     invocationOperation.Arguments[0]
                                                                        .Syntax.ToString()));
        }

        break;
      case "AsICollection":
        if (instanceIsSystem && Helpers.CheckNamespace(instance.Type,
                                                       "System",
                                                       "Collections",
                                                       "Generic",
                                                       "ICollection") && instance.Type?.NullableAnnotation is NullableAnnotation.NotAnnotated)
        {
          context.ReportDiagnostic(Diagnostic.Create(RedundantAsICollection,
                                                     context.Operation.Syntax.GetLocation(),
                                                     invocationOperation.Arguments[0]
                                                                        .Syntax.ToString()));
        }

        break;
    }
  }
}

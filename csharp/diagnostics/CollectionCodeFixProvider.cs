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

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Operations;

namespace ArmoniK.Utils.Diagnostics;

[ExportCodeFixProvider(LanguageNames.CSharp,
                       Name = nameof(CollectionCodeFixProvider))]
[Shared]
public class CollectionCodeFixProvider : CodeFixProvider
{
  public override ImmutableArray<string> FixableDiagnosticIds
    => ImmutableArray.Create(CollectionAnalyzer.RedundantAsICollection.Id,
                             CollectionAnalyzer.RedundantAsIList.Id);

  public override FixAllProvider? GetFixAllProvider()
    => WellKnownFixAllProviders.BatchFixer;

  public override async Task RegisterCodeFixesAsync(CodeFixContext context)
  {
    var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken)
                            .ConfigureAwait(false);

    context.RegisterCodeFix(CodeAction.Create("Remove Call",
                                              c => RemoveCalls(context.Document,
                                                               context.Diagnostics.Select(diagnostic =>
                                                                                          {
                                                                                            var diagnosticNode = root?.FindNode(diagnostic.Location.SourceSpan);
                                                                                            return diagnosticNode as InvocationExpressionSyntax;
                                                                                          }),
                                                               c),
                                              "Remove Call"),
                            context.Diagnostics);
  }

  private static async Task<Document> RemoveCalls(Document                                 document,
                                                  IEnumerable<InvocationExpressionSyntax?> invocations,
                                                  CancellationToken                        cancellationToken)
  {
    var editor = await DocumentEditor.CreateAsync(document,
                                                  cancellationToken)
                                     .ConfigureAwait(false);
    var semanticModel = await document.GetSemanticModelAsync(cancellationToken)
                                      .ConfigureAwait(false);

    foreach (var invocation in invocations)
    {
      if (invocation is null || semanticModel?.GetOperation(invocation) is not IInvocationOperation invocationOperation)
      {
        continue;
      }

      var exprArg = invocationOperation.Arguments[0];
      var expr    = exprArg.Syntax;

      if (expr is ArgumentSyntax arg)
      {
        expr = arg.ChildNodes()
                  .First();
      }

      editor.ReplaceNode(invocation,
                         expr);
    }

    return editor.GetChangedDocument();
  }
}

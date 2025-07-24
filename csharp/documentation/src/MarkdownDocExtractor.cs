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

using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;

namespace ArmoniK.Utils.DocExtractor;

/// <summary>
/// Utility class to generate Markdown documentation from C# source code
/// by extracting classes annotated with the <c>ExtractDocumentation</c> attribute.
/// </summary>
public abstract class MarkdownDocGenerator
{
    /// <summary>
    /// Parses the given solution file and generates a Markdown string
    /// documenting all classes with the <c>[ExtractDocumentation]</c> attribute
    /// and their properties.
    /// </summary>
    /// <param name="solutionPath">Path to the .sln file.</param>
    /// <returns>
    /// A Markdown-formatted string with environment variable documentation,
    /// or <c>null</c> if the solution file does not exist.
    /// </returns>
    public static async Task<string?> GenerateAsync(string solutionPath)
    {
        if (!File.Exists(solutionPath))
        {
          return null;
        }

        using var workspace = MSBuildWorkspace.Create();
        var solution = await workspace.OpenSolutionAsync(solutionPath).ConfigureAwait(false);

        var markdownBuilder = new StringBuilder();

        foreach (var project in solution.Projects)
        {
            foreach (var document in project.Documents)
            {
                var syntaxTree = await document.GetSyntaxTreeAsync().ConfigureAwait(false);
                if (syntaxTree == null)
                {
                  continue;
                }

                var root = await syntaxTree.GetRootAsync().ConfigureAwait(false);
                var markdown = GenerateFromSyntaxRoot(root);
                markdownBuilder.Append(markdown);
            }
        }

        return markdownBuilder.ToString();
    }

    /// <summary>
    /// Generates Markdown documentation from a Roslyn <see cref="SyntaxNode"/>.
    /// Only classes decorated with <c>[ExtractDocumentation]</c> will be processed.
    /// </summary>
    /// <param name="root">The root syntax node of the document.</param>
    /// <returns>A Markdown string documenting the class and its public properties.</returns>
    public static string GenerateFromSyntaxRoot(SyntaxNode root)
    {
        var markdownBuilder = new StringBuilder();

        var classes = root.DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .Where(c => c.AttributeLists
                .SelectMany(a => a.Attributes)
                .Any(attr => attr.Name.ToString() == "ExtractDocumentation"));

        foreach (var classDeclaration in classes)
        {
            var extractDocumentationAttribute = classDeclaration.AttributeLists
                .SelectMany(a => a.Attributes)
                .FirstOrDefault(attr => attr.Name.ToString() == "ExtractDocumentation");

            var description = extractDocumentationAttribute?.ArgumentList?.Arguments
                .FirstOrDefault()?.ToString().Trim('"');

            markdownBuilder.AppendLine($"## {description}");

            var properties = classDeclaration.Members.OfType<PropertyDeclarationSyntax>();
            foreach (var property in properties)
            {
                var type = property.Type.ToString();
                var name = property.Identifier.Text;

                var xmlComment = property.GetLeadingTrivia()
                    .Select(trivia => trivia.GetStructure())
                    .OfType<DocumentationCommentTriviaSyntax>()
                    .FirstOrDefault();

                var summary = xmlComment?.Content
                    .OfType<XmlElementSyntax>()
                    .FirstOrDefault(e => e.StartTag.Name.ToString() == "summary")?
                    .Content.ToFullString().Trim();
                summary = summary?.Replace("///", "");

                var envVar = $"{classDeclaration.Identifier.Text}__{name}";

                markdownBuilder.AppendLine($"- **{envVar}**: {type}");
                if (!string.IsNullOrEmpty(summary))
                {
                    markdownBuilder.AppendLine($"\n{summary}");
                }
            }

            markdownBuilder.AppendLine();
        }

        return markdownBuilder.ToString();
    }
}

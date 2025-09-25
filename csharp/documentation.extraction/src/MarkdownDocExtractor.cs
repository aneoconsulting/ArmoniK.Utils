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
///   Utility class to generate Markdown documentation from C# source code
///   by extracting classes annotated with the <c>ExtractDocumentation</c> attribute.
/// </summary>
public class MarkdownDocGenerator
{
  private readonly Dictionary<string, string?> docSections_;
  private readonly List<SyntaxNode>            syntaxRootNodes_;

  /// <summary>
  ///   Initializes a new instance of the <see cref="MarkdownDocGenerator" /> class.
  /// </summary>
  /// <param name="syntaxRootNodes">
  ///   A list of syntax root nodes representing the structure of the code
  ///   extracted from the solution's documents.
  /// </param>
  /// <param name="docSections">
  ///   A dictionary mapping class names to their corresponding documentation
  ///   section identifiers, which are used for generating Markdown links.
  /// </param>
  private MarkdownDocGenerator(List<SyntaxNode>            syntaxRootNodes,
                               Dictionary<string, string?> docSections)
  {
    syntaxRootNodes_ = syntaxRootNodes;
    docSections_     = docSections;
  }

  /// <summary>
  ///   Asynchronously creates an instance of <see cref="MarkdownDocGenerator" /> by opening a solution file,
  ///   collecting syntax root nodes, and extracting documentation descriptions from classes/enums decorated with
  ///   the <c>ExtractDocumentation</c> attribute.
  /// </summary>
  /// <param name="solutionPath">The file path to the solution (.sln) file.</param>
  /// <returns>
  ///   A task that represents the asynchronous operation. The task result contains an instance of
  ///   <see cref="MarkdownDocGenerator" /> initialized with the collected syntax root nodes and class documentation
  ///   sections.
  /// </returns>
  /// <exception cref="FileNotFoundException">Thrown when the specified solution file does not exist.</exception>
  /// <remarks>
  ///   This method uses <see cref="MSBuildWorkspace" /> to open the solution and retrieve its projects and documents.
  ///   It iterates through each document, extracting the syntax tree and collecting all class/enums declarations that
  ///   have the <c>ExtractDocumentation</c> attribute. The descriptions from these classes/enums are stored in a
  ///   dictionary for later use in generating Markdown documentation in case a public property of a class/enum is another
  ///   class/enum that has to be collected as well.
  /// </remarks>
  public static async Task<MarkdownDocGenerator> CreateAsync(string solutionPath)
  {
    if (!File.Exists(solutionPath))
    {
      throw new FileNotFoundException("Solution file not found",
                                      solutionPath);
    }

    using var workspace = MSBuildWorkspace.Create();
    var       solution  = await workspace.OpenSolutionAsync(solutionPath);

    var syntaxRootNodes = new List<SyntaxNode>();
    var docSections     = new Dictionary<string, string?>();

    // Collect all syntax root nodes and descriptions from each decorated class
    foreach (var project in solution.Projects)
    {
      foreach (var document in project.Documents)
      {
        var syntaxTree = await document.GetSyntaxTreeAsync()
                                       .ConfigureAwait(false);
        if (syntaxTree == null)
        {
          continue;
        }

        var root = await syntaxTree.GetRootAsync()
                                   .ConfigureAwait(false);
        syntaxRootNodes.Add(root);

        var types = root.DescendantNodes()
                        .Where(n => n is ClassDeclarationSyntax || n is EnumDeclarationSyntax)
                        .Cast<MemberDeclarationSyntax>()
                        .Where(decl => decl.AttributeLists.SelectMany(a => a.Attributes)
                                           .Any(attr => attr.Name.ToString() == "ExtractDocumentation"));

        // Collect documentation descriptions from all classes in all projects
        foreach (var decl in types)
        {
          var description = GetAttributeDescription(decl,
                                                    "ExtractDocumentation");

          // track name → description (used for Markdown anchors)
          docSections[decl switch
                      {
                        ClassDeclarationSyntax cls => cls.Identifier.Text,
                        EnumDeclarationSyntax en   => en.Identifier.Text,
                        _                          => string.Empty,
                      }] = description?.ToLower()
                                      ?.Replace(" ",
                                                "-");
        }
      }
    }

    return new MarkdownDocGenerator(syntaxRootNodes,
                                    docSections);
  }

  /// <summary>
  ///   Generates a Markdown string
  ///   documenting all classes with the <c>[ExtractDocumentation]</c> attribute
  ///   and their properties.
  /// </summary>
  /// <param name="customTitle">The title to be given to the generated document.</param>
  /// <returns>
  ///   A Markdown-formatted string with environment variable documentation,
  /// </returns>
  public string? Generate(string? customTitle = null)
  {
    var markdownBuilder = new StringBuilder();

    if (customTitle != null)
    {
      markdownBuilder.AppendLine($"# {customTitle}");
    }

    foreach (var root in syntaxRootNodes_)
    {
      var markdown = GenerateFromSyntaxRoot(root);
      markdownBuilder.Append(markdown);
    }

    return markdownBuilder.ToString();
  }

  /// <summary>
  ///   Retrieves the description string from a specified attribute applied to a declaration.
  /// </summary>
  /// <param name="decl">
  ///   The <see cref="MemberDeclarationSyntax" /> node (e.g., class or enum) to inspect.
  /// </param>
  /// <param name="attributeName">
  ///   The name of the attribute to search for (without the "Attribute" suffix).
  /// </param>
  /// <returns>
  ///   The attribute's first constructor argument as a trimmed string, or <c>null</c> if not found.
  /// </returns>
  private static string? GetAttributeDescription(MemberDeclarationSyntax decl,
                                                 string                  attributeName)
  {
    var attr = decl.AttributeLists.SelectMany(a => a.Attributes)
                   .FirstOrDefault(attr => attr.Name.ToString() == attributeName);

    return attr?.ArgumentList?.Arguments.FirstOrDefault()
               ?.ToString()
               .Trim('"');
  }

  /// <summary>
  ///   Extracts the XML documentation summary associated with a syntax node.
  /// </summary>
  /// <param name="node">
  ///   The <see cref="SyntaxNode" /> to inspect (e.g., property, enum member).
  /// </param>
  /// <returns>
  ///   The contents of the <c>&lt;summary&gt;</c> XML documentation element as plain text,
  ///   or <c>null</c> if no summary is found.
  /// </returns>
  private static string? GetXmlSummary(SyntaxNode node)
  {
    var xmlComment = node.GetLeadingTrivia()
                         .Select(trivia => trivia.GetStructure())
                         .OfType<DocumentationCommentTriviaSyntax>()
                         .FirstOrDefault();

    var summary = xmlComment?.Content.OfType<XmlElementSyntax>()
                            .FirstOrDefault(e => e.StartTag.Name.ToString() == "summary")
                            ?.Content.ToFullString()
                            .Trim();

    return summary?.Replace("///",
                            "");
  }

  /// <summary>
  ///   Generates Markdown documentation from a Roslyn <see cref="SyntaxNode" />.
  ///   Only classes and enums decorated with <c>[ExtractDocumentation]</c> will be processed.
  /// </summary>
  /// <param name="root">The root syntax node of the document.</param>
  /// <returns>A Markdown string documenting the class and its public properties.</returns>
  private string GenerateFromSyntaxRoot(SyntaxNode root)
  {
    var markdownBuilder = new StringBuilder();

    var types = root.DescendantNodes()
                    .Where(n => n is ClassDeclarationSyntax or EnumDeclarationSyntax)
                    .Cast<MemberDeclarationSyntax>()
                    .Where(decl => decl.AttributeLists.SelectMany(a => a.Attributes)
                                       .Any(attr => attr.Name.ToString() == "ExtractDocumentation"));

    foreach (var decl in types)
    {
      var description = GetAttributeDescription(decl,
                                                "ExtractDocumentation");
      markdownBuilder.AppendLine($"## {description}");

      switch (decl)
      {
        case ClassDeclarationSyntax classDecl:
        {
          foreach (var property in classDecl.Members.OfType<PropertyDeclarationSyntax>())
          {
            var type    = property.Type.ToString();
            var name    = property.Identifier.Text;
            var summary = GetXmlSummary(property);
            var envVar  = $"{classDecl.Identifier.Text}__{name}";

            if (docSections_.TryGetValue(type,
                                         out var desc))
            {
              markdownBuilder.AppendLine($"- **{envVar}**: [{type}](#{desc})");
            }
            else
            {
              markdownBuilder.AppendLine($"- **{envVar}**: {type}");
            }

            if (!string.IsNullOrEmpty(summary))
            {
              markdownBuilder.AppendLine($"\n{summary}");
            }
          }

          break;
        }
        case EnumDeclarationSyntax enumDecl:
        {
          foreach (var member in enumDecl.Members)
          {
            var name    = member.Identifier.Text;
            var summary = GetXmlSummary(member);

            markdownBuilder.AppendLine($"- **{name}**");

            if (!string.IsNullOrEmpty(summary))
            {
              markdownBuilder.AppendLine($"\n{summary}");
            }
          }

          break;
        }
      }

      markdownBuilder.AppendLine();
    }

    return markdownBuilder.ToString();
  }
}

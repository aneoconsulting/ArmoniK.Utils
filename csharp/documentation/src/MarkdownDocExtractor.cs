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
  private readonly Dictionary<string, string?> classDocSections_;
  private readonly List<SyntaxNode>            syntaxRootNodes_;

  /// <summary>
  ///   Initializes a new instance of the <see cref="MarkdownDocGenerator" /> class.
  /// </summary>
  /// <param name="syntaxRootNodes">
  ///   A list of syntax root nodes representing the structure of the code
  ///   extracted from the solution's documents.
  /// </param>
  /// <param name="classDocSections">
  ///   A dictionary mapping class names to their corresponding documentation
  ///   section identifiers, which are used for generating Markdown links.
  /// </param>
  private MarkdownDocGenerator(List<SyntaxNode>            syntaxRootNodes,
                               Dictionary<string, string?> classDocSections)
  {
    syntaxRootNodes_  = syntaxRootNodes;
    classDocSections_ = classDocSections;
  }

  /// <summary>
  ///   Asynchronously creates an instance of <see cref="MarkdownDocGenerator" /> by opening a solution file,
  ///   collecting syntax root nodes, and extracting documentation descriptions from classes decorated with
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
  ///   It iterates through each document, extracting the syntax tree and collecting all class declarations that
  ///   have the <c>ExtractDocumentation</c> attribute. The descriptions from these classes are stored in a
  ///   dictionary for later use in generating Markdown documentation in case a public property of a class is another
  ///   class that has to be collected as well.
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

    var syntaxRootNodes  = new List<SyntaxNode>();
    var classDocSections = new Dictionary<string, string?>();

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

        var classes = root.DescendantNodes()
                          .OfType<ClassDeclarationSyntax>()
                          .Where(c => c.AttributeLists.SelectMany(a => a.Attributes)
                                       .Any(attr => attr.Name.ToString() == "ExtractDocumentation"));

        // Collect documentation descriptions from all classes in all projects
        foreach (var classDeclaration in classes)
        {
          var extractDocumentationAttribute = classDeclaration.AttributeLists.SelectMany(a => a.Attributes)
                                                              .FirstOrDefault(attr => attr.Name.ToString() == "ExtractDocumentation");

          var description = extractDocumentationAttribute?.ArgumentList?.Arguments.FirstOrDefault()
                                                         ?.ToString()
                                                         .Trim('"');

          // Add this class to tracking dictionary and build a Markdown link to it
          classDocSections[classDeclaration.Identifier.Text] = description?.ToLower()
                                                                          .Replace(" ",
                                                                                   "-");
        }
      }
    }

    return new MarkdownDocGenerator(syntaxRootNodes,
                                    classDocSections);
  }

  /// <summary>
  ///   Generates a Markdown string
  ///   documenting all classes with the <c>[ExtractDocumentation]</c> attribute
  ///   and their properties.
  /// </summary>
  /// <returns>
  ///   A Markdown-formatted string with environment variable documentation,
  /// </returns>
  public string? Generate()
  {
    var markdownBuilder = new StringBuilder();
    foreach (var root in syntaxRootNodes_)
    {
      var markdown = GenerateFromSyntaxRoot(root);
      markdownBuilder.Append(markdown);
    }

    return markdownBuilder.ToString();
  }

  /// <summary>
  ///   Generates Markdown documentation from a Roslyn <see cref="SyntaxNode" />.
  ///   Only classes decorated with <c>[ExtractDocumentation]</c> will be processed.
  /// </summary>
  /// <param name="root">The root syntax node of the document.</param>
  /// <returns>A Markdown string documenting the class and its public properties.</returns>
  private string GenerateFromSyntaxRoot(SyntaxNode root)
  {
    var markdownBuilder = new StringBuilder();

    var classes = root.DescendantNodes()
                      .OfType<ClassDeclarationSyntax>()
                      .Where(c => c.AttributeLists.SelectMany(a => a.Attributes)
                                   .Any(attr => attr.Name.ToString() == "ExtractDocumentation"));

    foreach (var classDeclaration in classes)
    {
      var extractDocumentationAttribute = classDeclaration.AttributeLists.SelectMany(a => a.Attributes)
                                                          .FirstOrDefault(attr => attr.Name.ToString() == "ExtractDocumentation");

      var description = extractDocumentationAttribute?.ArgumentList?.Arguments.FirstOrDefault()
                                                     ?.ToString()
                                                     .Trim('"');

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

        var summary = xmlComment?.Content.OfType<XmlElementSyntax>()
                                .FirstOrDefault(e => e.StartTag.Name.ToString() == "summary")
                                ?.Content.ToFullString()
                                .Trim();
        summary = summary?.Replace("///",
                                   "");

        var envVar = $"{classDeclaration.Identifier.Text}__{name}";

        // Check if the property type is a class with documentation
        if (classDocSections_.TryGetValue(type,
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

      markdownBuilder.AppendLine();
    }

    return markdownBuilder.ToString();
  }
}

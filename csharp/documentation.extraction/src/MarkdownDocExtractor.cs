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
  /// <summary>
  ///   Sections to extract from the XML documentation
  /// </summary>
  private readonly List<string> sectionsToExtract_ = ["summary", "remarks", "example"];

  private readonly Dictionary<string, MemberDeclarationSyntax> syntaxTypes_;

  /// <summary>
  ///   Initializes a new instance of the <see cref="MarkdownDocGenerator" /> class.
  /// </summary>
  /// <param name="syntaxTypes">
  ///   A dictionary of syntax root nodes representing the structure of the code
  ///   extracted from the solution's documents. The key is the class name and
  ///   the value is the corresponding <see cref="MemberDeclarationSyntax" />.
  /// </param>
  private MarkdownDocGenerator(Dictionary<string, MemberDeclarationSyntax> syntaxTypes)
    => syntaxTypes_ = syntaxTypes;

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

    var syntaxTypes = new Dictionary<string, MemberDeclarationSyntax>();

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

        var types = root.DescendantNodes()
                        .Where(n => n is ClassDeclarationSyntax or EnumDeclarationSyntax)
                        .Cast<MemberDeclarationSyntax>()
                        .Where(decl => decl.AttributeLists.SelectMany(a => a.Attributes)
                                           .Any(attr => attr.Name.ToString() == "ExtractDocumentation"))
                        .ToDictionary(decl => decl switch
                                              {
                                                ClassDeclarationSyntax c => c.Identifier.Text,
                                                EnumDeclarationSyntax e  => e.Identifier.Text,
                                                _                        => "",
                                              });

        // Merge all member declaration syntax dictionaries in a global one for latter use.
        foreach (var type in types)
        {
          syntaxTypes[type.Key] = type.Value;
        }
      }
    }

    return new MarkdownDocGenerator(syntaxTypes);
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

    var markdown = GenerateFromSyntaxTypes();
    markdownBuilder.Append(markdown);

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
  ///   Extracts the XML documentation associated with a syntax node.
  /// </summary>
  /// <param name="node">
  ///   The <see cref="SyntaxNode" /> to inspect (e.g., property, enum member).
  /// </param>
  /// <param name="sectionName">
  ///   The name of the section to extract.
  /// </param>
  /// <returns>
  ///   The contents of the <c>&lt;sectionName&gt;</c> XML documentation element as plain text,
  ///   or <c>null</c> if no section with the given name is found.
  /// </returns>
  private static string? GetXmlDocumentation(SyntaxNode node,
                                             string     sectionName = "summary")
  {
    var xmlComment = node.GetLeadingTrivia()
                         .Select(trivia => trivia.GetStructure())
                         .OfType<DocumentationCommentTriviaSyntax>()
                         .FirstOrDefault();

    if (xmlComment == null)
    {
      return null;
    }

    // Find all matching sections
    var sections = xmlComment.Content.OfType<XmlElementSyntax>()
                             .Where(e => e.StartTag.Name.ToString() == sectionName)
                             .ToList();

    if (sections.Count == 0)
    {
      return null;
    }

    var allSections = sections.Select(section => section.Content.ToFullString()
                                                        .Trim())
                              .ToList();

    var cleanedSections = allSections.SelectMany(section => section.Split(['\r', '\n'],
                                                                          StringSplitOptions.RemoveEmptyEntries))
                                     .Select(line => line.Replace("///",
                                                                  "")
                                                         .Trim())
                                     .Where(line => !string.IsNullOrWhiteSpace(line))
                                     .ToList();

    return string.Join("\n",
                       cleanedSections);
  }


  // Get the default value for common value types
  private static string GetDefaultValueForType(string typeName)
    => typeName switch
       {
         "int"     => "0",
         "float"   => "0.0f",
         "double"  => "0.0",
         "bool"    => "false",
         "string"  => string.Empty,
         "char"    => "'\\0'",
         "decimal" => "0.0m",
         _         => "null", // Default for reference types
       };

  /// <summary>
  ///   Iterates over all properties in the given class declaration and appends a flattened
  ///   representation of each one to the specified <see cref="StringBuilder" />.
  /// </summary>
  /// <param name="builder">The output buffer used to collect flattened property definitions.</param>
  /// <param name="classDecl">The class whose properties will be inspected.</param>
  /// <param name="prefix">The name prefix used to construct the flattened property path.</param>
  private void FlattenProperties(StringBuilder           builder,
                                 ClassDeclarationSyntax? classDecl,
                                 string                  prefix)
  {
    if (classDecl == null)
    {
      return;
    }

    foreach (var property in classDecl.Members.OfType<PropertyDeclarationSyntax>())
    {
      var typeName     = property.Type.ToString();
      var propertyName = property.Identifier.Text;
      var fullName     = $"{prefix}__{propertyName}";

      var initializer = property.Initializer;

      // Check if the default value is an ObjectCreationExpressionSyntax
      var defaultValue = initializer?.Value.ToString() switch
                         {
                           "new()" => "()", // Display "()" instead of "new()" as default value
                           _       => initializer?.Value.ToString() ?? GetDefaultValueForType(typeName),
                         };

      // If the property is also a class, flatten its members recursively.
      if (syntaxTypes_.TryGetValue(typeName,
                                   out var memberDecl))
      {
        var description = GetAttributeDescription(memberDecl,
                                                  "ExtractDocumentation");
        var markdownLink = description?.ToLower()
                                      ?.Replace(" ",
                                                "-");
        {
          // Build a link to the "parent" declaration
          fullName = $"{prefix}__[{propertyName}](#{markdownLink})";
          FlattenProperties(builder,
                            memberDecl as ClassDeclarationSyntax,
                            fullName);
          continue;
        }
      }

      builder.AppendLine($"\n- **{fullName}**: {typeName} (default: `{defaultValue}`)\n");
      foreach (var sectionName in sectionsToExtract_)
      {
        var section = GetXmlDocumentation(property,
                                          sectionName);
        if (!string.IsNullOrEmpty(section))
        {
          builder.AppendLine($"    {section.Trim()}");
        }
      }
    }
  }

  /// <summary>
  ///   Generates Markdown documentation from a Roslyn <see cref="SyntaxNode" />.
  ///   Only classes and enums decorated with <c>[ExtractDocumentation]</c> will be processed.
  /// </summary>
  /// <returns>A Markdown string documenting the class and its public properties.</returns>
  private string GenerateFromSyntaxTypes()
  {
    var markdownBuilder = new StringBuilder();

    foreach (var decl in syntaxTypes_.Values)
    {
      var description = GetAttributeDescription(decl,
                                                "ExtractDocumentation");
      markdownBuilder.AppendLine($"## {description}");

      switch (decl)
      {
        case ClassDeclarationSyntax classDecl:
        {
          FlattenProperties(markdownBuilder,
                            classDecl,
                            classDecl.Identifier.Text);
          break;
        }
        case EnumDeclarationSyntax enumDecl:
        {
          foreach (var member in enumDecl.Members)
          {
            var name = member.Identifier.Text;
            markdownBuilder.AppendLine($"\n- **{name}**\n");

            foreach (var sectionName in sectionsToExtract_)
            {
              var section = GetXmlDocumentation(member,
                                                sectionName);
              if (!string.IsNullOrEmpty(section))
              {
                markdownBuilder.AppendLine($"    {section.Trim()}");
              }
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

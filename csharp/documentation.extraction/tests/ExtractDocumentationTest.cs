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

using NUnit.Framework;

namespace ArmoniK.Utils.DocExtractor.Tests;

/// <summary>
///   Base class for extraction tests, providing common setup and teardown functionality.
/// </summary>
public abstract class ExtractionTestBase
{
  protected string TempSolutionPath;

  [SetUp]
  public void Setup()
  {
    // Create a temporary solution path
    TempSolutionPath = Path.Combine(Path.GetTempPath(),
                                    "TempSolution.sln");
    CreateTemporarySolution(TempSolutionPath);
  }

  [TearDown]
  public void TearDown()
  {
    if (File.Exists(TempSolutionPath))
    {
      File.Delete(TempSolutionPath);
    }
  }

  /// <summary>
  ///   Creates a temporary solution.
  /// </summary>
  /// <param name="solutionPath">The path where the temporary solution will be created.</param>
  private void CreateTemporarySolution(string solutionPath)
  {
    // Create a temporary project with classes decorated with the ExtractDocumentation attribute
    var projectPath = Path.Combine(Path.GetDirectoryName(solutionPath) ?? string.Empty,
                                   "TempProject.csproj");

    // Derived classes should implement the code to generate a test class code
    var classCode = GenerateClassCode();

    // Write the project file and class file
    File.WriteAllText(projectPath,
                      """
                      <Project Sdk="Microsoft.NET.Sdk">
                          <PropertyGroup>
                              <OutputType>Library</OutputType>
                              <TargetFramework>net8.0</TargetFramework>
                          </PropertyGroup>
                      </Project>
                      """);

    var classFilePath = Path.Combine(Path.GetDirectoryName(projectPath) ?? string.Empty,
                                     "TestClass.cs");
    File.WriteAllText(classFilePath,
                      classCode);

    // Create the solution file
    File.WriteAllText(solutionPath,
                      "Microsoft Visual Studio Solution File, Format Version 12.00\nProject(\"{GUID}\") = \"TempProject\", \"TempProject.csproj\", \"{GUID}\"\nEndProject\n");
  }

  /// <summary>
  ///   Generates the class code for the temporary project. This method must be implemented by derived classes.
  /// </summary>
  /// <returns>The class code as a string.</returns>
  protected abstract string GenerateClassCode();

  /// <summary>
  ///   Normalizes the specified string by replacing Windows-style line endings with Unix-style line endings
  ///   and trimming whitespace from both ends.
  /// </summary>
  /// <param name="s">The string to normalize. Can be null.</param>
  /// <returns>
  ///   The normalized string. If the input string is null, returns an empty string.
  /// </returns>
  protected static string Normalize(string? s)
    => s?.Replace("\r\n",
                  "\n")
        .Trim() ?? "";
}

/// <summary>
///   Tests the extraction of nested classes and enums.
/// </summary>
[TestFixture]
public class NestedClassAndEnumsExtractionTests : ExtractionTestBase
{
  protected override string GenerateClassCode()
    => """
       using System;

       [ExtractDocumentation("Options for Awesome Class")]
       public class AwesomeClass
       {
           /// <summary>
           /// This is a nested property example.
           /// </summary>
           public HelperClass Help { get; set; }
       }

       [ExtractDocumentation("Options for Helper Class")]
       public class HelperClass
       {
           /// <summary>
           /// This is a test property
           /// </summary>
           public string Path { get; set; } = "localhost";

           /// <summary>
           /// This is another test property
           /// </summary>
           public int Port { get; set; } = 666;
       }

       [ExtractDocumentation("Options for Enum")]
       public enum HelperEnum
       {
           /// <summary>
           /// This is a test member
           /// </summary>
           Member1,

           /// <summary>
           /// This is another test member
           /// </summary>
           Member2
       }

       public class ExtractDocumentationAttribute : Attribute
       {
           public ExtractDocumentationAttribute(string description) { }
       }
       """;

  [Test]
  public Task CreateAsyncInvalidSolutionPathThrows()
  {
    const string solutionPath = "path/to/invalid/solution.sln";
    var          exception    = Assert.ThrowsAsync<FileNotFoundException>(() => MarkdownDocGenerator.CreateAsync(solutionPath));
    Assert.Multiple(() =>
                    {
                      Assert.That(exception.Message,
                                  Is.EqualTo("Solution file not found"));
                      Assert.That(exception.FileName,
                                  Is.EqualTo(solutionPath));
                    });
    return Task.CompletedTask;
  }

  /// <summary>
  ///   Tests the extraction of the default values for common types.
  /// </summary>
  [Test]
  public async Task NestedClassesAndEnumsExtractionIsCorrect()
  {
    var generator = await MarkdownDocGenerator.CreateAsync(TempSolutionPath);
    var markdown  = generator.Generate();

    const string expected = """
                            ## Options for Awesome Class
                            - **AwesomeClass__[Help](#options-for-helper-class)__Path**: string (default: `"localhost"`)
                                This is a test property
                            - **AwesomeClass__[Help](#options-for-helper-class)__Port**: int (default: `666`)
                                This is another test property

                            ## Options for Helper Class
                            - **HelperClass__Path**: string (default: `"localhost"`)
                                This is a test property
                            - **HelperClass__Port**: int (default: `666`)
                                This is another test property

                            ## Options for Enum
                            - **Member1**
                                This is a test member
                            - **Member2**
                                This is another test member

                            """;

    Assert.That(Normalize(markdown),
                Is.EqualTo(Normalize(expected)));
  }
}

[TestFixture]
public class DefaultValuesExtractionTests : ExtractionTestBase
{
  protected override string GenerateClassCode()
    => """
       using System;

       [ExtractDocumentation("Test Default Values Class")]
       public class TestDefaultsHelperClass
       {
           public int MyInt { get; set; }
           public bool MyBool { get; set; }
           public float myFloat { get; set; }
           public double myDouble { get; set; }
           public decimal myDecimal { get; set; }
           public char myChar { get; set; }
           public string MyString { get; set; }
           public string? MyNullableString { get; set; }
           public List<char> MyCharList { get; set; }
           public List<char> MyIntList { get; set; } = new();
       }

       public class ExtractDocumentationAttribute : Attribute
       {
           public ExtractDocumentationAttribute(string description) { }
       }
       """;

  [Test]
  public async Task DefaultValuesExtractionIsCorrect()
  {
    var generator = await MarkdownDocGenerator.CreateAsync(TempSolutionPath);
    var markdown  = generator.Generate();

    const string expected = """
                            ## Test Default Values Class
                            - **TestDefaultsHelperClass__MyInt**: int (default: `0`)
                            - **TestDefaultsHelperClass__MyBool**: bool (default: `false`)
                            - **TestDefaultsHelperClass__myFloat**: float (default: `0.0f`)
                            - **TestDefaultsHelperClass__myDouble**: double (default: `0.0`)
                            - **TestDefaultsHelperClass__myDecimal**: decimal (default: `0.0m`)
                            - **TestDefaultsHelperClass__myChar**: char (default: `'\0'`)
                            - **TestDefaultsHelperClass__MyString**: string (default: ``)
                            - **TestDefaultsHelperClass__MyNullableString**: string? (default: `null`)
                            - **TestDefaultsHelperClass__MyCharList**: List<char> (default: `null`)
                            - **TestDefaultsHelperClass__MyIntList**: List<char> (default: `()`)
                            """;

    Assert.That(Normalize(markdown),
                Is.EqualTo(Normalize(expected)));
  }
}

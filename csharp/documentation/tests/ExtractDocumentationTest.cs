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

[TestFixture]
public class MarkdownDocGeneratorTests
{
  private string tempSolutionPath_;

  [SetUp]
  public void Setup()
  {
    // Create a temporary solution path
    tempSolutionPath_ = Path.Combine(Path.GetTempPath(), "TempSolution.sln");
    CreateTemporarySolution(tempSolutionPath_);
  }
  private static void CreateTemporarySolution(string solutionPath)
  {
    // Create a temporary project with two classes decorated with the ExtractDocumentation attribute
    var projectPath = Path.Combine(Path.GetDirectoryName(solutionPath) ?? string.Empty, "TempProject.csproj");
    const string classCode = """
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
                            public string Path { get; set; }

                            /// <summary>
                            /// This is another test property
                            /// </summary>
                            public int Port { get; set; }
                        }

                        public class ExtractDocumentationAttribute : Attribute
                        {
                            public ExtractDocumentationAttribute(string description) { }
                        }
                        """;

    // Write the project file and class file
    File.WriteAllText(projectPath, """

                                               <Project Sdk="Microsoft.NET.Sdk">
                                                   <PropertyGroup>
                                                       <OutputType>Library</OutputType>
                                                       <TargetFramework>net8.0</TargetFramework>
                                                   </PropertyGroup>
                                               </Project>
                                   """);

    var classFilePath = Path.Combine(Path.GetDirectoryName(projectPath) ?? string.Empty, "TestClass.cs");
    File.WriteAllText(classFilePath, classCode);

    // Create the solution file
    File.WriteAllText(solutionPath, $"Microsoft Visual Studio Solution File, Format Version 12.00\nProject(\"{{GUID}}\") = \"TempProject\", \"TempProject.csproj\", \"{{GUID}}\"\nEndProject\n");
  }

  [TearDown]
  public void TearDown()
  {
    if (File.Exists(tempSolutionPath_))
    {
      File.Delete(tempSolutionPath_);
    }
  }

  [Test]
  public Task CreateAsyncInvalidSolutionPathThrows()
  {
    const string solutionPath = "path/to/invalid/solution.sln";
    var exception = Assert.ThrowsAsync<FileNotFoundException>(
                                                              () => MarkdownDocGenerator.CreateAsync(solutionPath));
    Assert.Multiple(() =>
    {
      Assert.That(exception.Message, Is.EqualTo("Solution file not found"));
      Assert.That(exception.FileName, Is.EqualTo(solutionPath));
    });
    return Task.CompletedTask;
  }

  [Test]
  public async Task GenerateFromSyntaxRootShouldExtractDocsCorrectly()
  {
    var generator = await MarkdownDocGenerator.CreateAsync(tempSolutionPath_);
    var markdown  = generator.Generate();

    Assert.That(markdown, Does.Contain("## Options for Awesome Class"));
    Assert.That(markdown, Does.Contain("**AwesomeClass__Help**: [HelperClass](#options-for-helper-class)"));
    Assert.That(markdown, Does.Contain("This is a nested property example"));
    Assert.That(markdown, Does.Contain("## Options for Helper Class"));
    Assert.That(markdown, Does.Contain("This is a test property"));
    Assert.That(markdown, Does.Contain("This is another test property"));
  }
}

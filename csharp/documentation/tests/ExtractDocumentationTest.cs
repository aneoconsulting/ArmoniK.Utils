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

using Microsoft.CodeAnalysis.CSharp;

using NUnit.Framework;

namespace ArmoniK.Utils.DocExtractor.Tests;

[TestFixture]
public class MarkdownDocGeneratorTests
{
  [Test]
  public void GenerateFromSyntaxRootShouldExtractDocsCorrectly()
  {
    const string code = """

                        using System;

                        [ExtractDocumentation("Test Description")]
                        public class TestClass
                        {
                            /// <summary>This is a test property</summary>
                            public string Name { get; set; }
                        }

                        public class ExtractDocumentationAttribute : Attribute
                        {
                            public ExtractDocumentationAttribute(string description) { }
                        }

                        """;

    var syntaxTree      = CSharpSyntaxTree.ParseText(code);
    var root = syntaxTree.GetRoot();
    var markdown  = MarkdownDocGenerator.GenerateFromSyntaxRoot(root);

    Assert.That(markdown, Does.Contain("## Test Description"));
    Assert.That(markdown, Does.Contain("**TestClass__Name**"));
    Assert.That(markdown, Does.Contain("This is a test property"));
  }
}

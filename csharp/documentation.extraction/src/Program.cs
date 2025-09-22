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

using CommandLine;

namespace ArmoniK.Utils.DocExtractor;

// ReSharper disable once ClassNeverInstantiated.Global
internal class Options
{
  [Option('s', "solutionPath", Required = true, HelpText = "Path to solution file")]
  public string? SolutionPath { get; set; }

  [Option('t', "title", Required = false, HelpText = "Custom title for the extracted document")]
  public string? Title { get; set; }
}

internal abstract class Program
{
  private static async Task Main(string[] args)
  {
    await Parser.Default.ParseArguments<Options>(args)
               .WithParsedAsync(async o =>
                                {
                                  var generator        = await MarkdownDocGenerator.CreateAsync(o.SolutionPath!);
                                  var solutionRootName = Path.GetFileNameWithoutExtension(o.SolutionPath!);
                                  var customTitle= o.Title ?? $"Environment Variables exposed in {solutionRootName}";
                                  var markdown  = generator.Generate(customTitle);
                                  var outputFileName   = solutionRootName + ".EnvVars.md";
                                  await File.WriteAllTextAsync(outputFileName,
                                                               markdown);
                                  Console.WriteLine($"Markdown file generated: {outputFileName}");
                                }).ConfigureAwait(false);
  }
}

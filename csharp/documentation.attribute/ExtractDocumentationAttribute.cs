// This file is part of the ArmoniK project
//
// Copyright (C) ANEO, 2022-$CURRENT_YEAR.All rights reserved.
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

using System;

namespace ArmoniK.Utils.DocAttribute;

/// <summary>
///   Indicates that a class or enum should have its property documentation collected.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Enum,
                Inherited = false)]
public class ExtractDocumentationAttribute : Attribute
{
  /// <summary>
  ///   Initializes a new instance of the <see cref="ExtractDocumentationAttribute" /> class.
  /// </summary>
  /// <param name="description">A description for the attribute, providing context about the class. Must not be empty.</param>
  /// <exception cref="ArgumentException">Thrown when the description is an empty string.</exception>
  public ExtractDocumentationAttribute(string description)
  {
    if (string.IsNullOrWhiteSpace(description))
    {
      throw new ArgumentException("Description must not be empty.",
                                  nameof(description));
    }

    Description = description;
  }

  /// <summary>
  ///   Gets the description of the attribute.
  /// </summary>
  public string Description { get; }
}

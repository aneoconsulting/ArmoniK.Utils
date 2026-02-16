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

using System.Linq;

using Microsoft.CodeAnalysis;

namespace ArmoniK.Utils.Diagnostics;

public static class Helpers
{
  public static bool CheckNamespace(INamespaceOrTypeSymbol? ns,
                                    params string[]         elements)
  {
    foreach (var element in elements.Reverse())
    {
      if (ns?.Name != element)
      {
        return false;
      }

      ns = ns?.ContainingNamespace;
    }

    return (ns as INamespaceSymbol)?.IsGlobalNamespace == true;
  }
}

// This file is part of the ArmoniK project
//
// Copyright (C) ANEO, 2022-2024.All rights reserved.
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

// ReSharper disable UnusedType.Global
// ReSharper disable UnusedMember.Global

namespace ArmoniK.Utils.Diagnostics.Sample;

// If you don't see warnings, build the Analyzers Project.

public class Examples
{
  public void ToStars()
  {
    var test = new[]
               {
                 1,
               }.AsICollection()
                .AsICollection();
    var foo = test.AsICollection();
  }
}

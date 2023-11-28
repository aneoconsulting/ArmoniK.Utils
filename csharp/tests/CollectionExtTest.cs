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

using System.Collections.Generic;

using NUnit.Framework;

namespace ArmoniK.Utils.Tests;

public class CollectionExtTest
{
  [Test]
  public void ViewSelectShouldWork()
  {
    ICollection<int> collection = new List<int>();
    var              view       = collection.ViewSelect(x => -x);

    Assert.That(view,
                Is.Empty);

    collection.Add(1);
    collection.Add(2);

    Assert.That(view,
                Is.EqualTo(new[]
                           {
                             -1,
                             -2,
                           }));

    Assert.That(view,
                Does.Contain(-1));
    Assert.That(view,
                Does.Contain(-2));

    collection.Remove(1);

    Assert.That(view,
                Is.EqualTo(new[]
                           {
                             -2,
                           }));

    Assert.That(view,
                Does.Not.Contain(-1));
    Assert.That(view,
                Does.Contain(-2));
  }
}

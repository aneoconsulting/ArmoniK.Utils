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

using System.Threading;

using JetBrains.Annotations;

namespace ArmoniK.Utils;

/// <summary>
///   Trigger to yield the elements buffered by a chunking enumeration early, as if its delay had expired.
/// </summary>
/// <remarks>
///   A flush only applies to the elements already buffered. A flusher can be shared between enumerations, and
///   <see cref="Flush" /> can be called from any thread.
/// </remarks>
[PublicAPI]
public sealed class ChunkFlusher
{
  private CancellationTokenSource cts_ = new();

  // Cancelled at the next call to Flush
  internal CancellationToken Token
    => Volatile.Read(ref cts_)
               .Token;

  /// <summary>
  ///   Yield the elements buffered by the enumerations using this flusher as soon as possible.
  /// </summary>
  public void Flush()
    => Interlocked.Exchange(ref cts_,
                            new CancellationTokenSource())
                  .Cancel();
}

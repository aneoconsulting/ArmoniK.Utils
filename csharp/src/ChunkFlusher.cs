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
///   Trigger to yield the data currently buffered by a chunking operation, as if its timeout had expired.
/// </summary>
/// <remarks>
///   <para>
///     A flush applies to the data buffered at the time of the request: if nothing is buffered, it has no effect,
///     and data buffered afterward is not affected.
///   </para>
///   <para>
///     The same <see cref="ChunkFlusher" /> can be shared between several chunking operations, and
///     <see cref="Flush" /> can be called from any thread.
///   </para>
/// </remarks>
[PublicAPI]
public sealed class ChunkFlusher
{
  private CancellationTokenSource cts_ = new();

  /// <summary>
  ///   Token cancelled at the next call to <see cref="Flush" />
  /// </summary>
  internal CancellationToken Token
    => Volatile.Read(ref cts_)
               .Token;

  /// <summary>
  ///   Request the chunking operations using this flusher to yield their buffered data as soon as possible.
  /// </summary>
  public void Flush()
    => Interlocked.Exchange(ref cts_,
                            new CancellationTokenSource())
                  .Cancel();
}

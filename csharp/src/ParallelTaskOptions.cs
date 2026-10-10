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

using System;
using System.Threading;

using JetBrains.Annotations;

namespace ArmoniK.Utils;

/// <summary>
///   Options for ParallelSelect and ParallelWait
/// </summary>
[PublicAPI]
public struct ParallelTaskOptions
{
  private readonly int parallelismLimit_ = 0;
  private readonly int bufferLimit_      = 0;

  /// <summary>Limit the parallelism for ParallelSelect</summary>
  public int ParallelismLimit
  {
    get
      => parallelismLimit_ switch
         {
           < 0 => int.MaxValue,
           0   => Environment.ProcessorCount,
           _   => parallelismLimit_,
         };
    init => parallelismLimit_ = value;
  }

  /// <summary>
  ///   Limit the number of results held by ParallelSelect at any given moment:
  ///   tasks that have been started and whose result has not been yielded yet, including the running ones.
  /// </summary>
  /// <remarks>
  ///   <para>
  ///     If BufferLimit is 0 (default), <see cref="ParallelismLimit" /> is used as the limit.
  ///     If BufferLimit is negative, no limit is enforced.
  ///   </para>
  ///   <para>
  ///     A BufferLimit larger than <see cref="ParallelismLimit" /> lets new tasks start while completed results
  ///     are waiting to be yielded, for instance behind a slow task when the results are ordered.
  ///     A BufferLimit lower than <see cref="ParallelismLimit" /> also limits the parallelism.
  ///   </para>
  /// </remarks>
  public int BufferLimit
  {
    get
      => bufferLimit_ switch
         {
           < 0 => int.MaxValue,
           0   => ParallelismLimit,
           _   => bufferLimit_,
         };
    init => bufferLimit_ = value;
  }

  /// <summary>Cancellation token used for stopping the enumeration</summary>
  public CancellationToken CancellationToken { get; init; } = default;

  /// <summary>Whether results order respect input order or not</summary>
  public bool Unordered { get; init; } = false;

  /// <summary>
  ///   Options for ParallelSelect and ParallelWait.
  ///   If parallelismLimit is 0, the number of threads is used as the limit.
  ///   If parallelismLimit is negative, no limit is enforced.
  /// </summary>
  /// <param name="parallelismLimit">Limit the parallelism</param>
  /// <param name="cancellationToken">Cancellation token used for stopping the enumeration</param>
  public ParallelTaskOptions(int               parallelismLimit,
                             CancellationToken cancellationToken = default)
  {
    ParallelismLimit  = parallelismLimit;
    CancellationToken = cancellationToken;
  }

  /// <summary>
  ///   Options for ParallelSelect and ParallelWait.
  ///   Parallelism is limited to the number of threads.
  /// </summary>
  /// <param name="cancellationToken">Cancellation token used for stopping the enumeration</param>
  public ParallelTaskOptions(CancellationToken cancellationToken = default)
    : this(0,
           cancellationToken)
  {
  }


  /// <summary>
  ///   Options for ParallelSelect and ParallelWait.
  ///   If parallelismLimit is 0, the number of threads is used as the limit.
  ///   If parallelismLimit is negative, no limit is enforced.
  /// </summary>
  /// <param name="unordered">If true, the result order does not respect input order</param>
  /// <param name="parallelismLimit">Limit the parallelism</param>
  /// <param name="cancellationToken">Cancellation token used for stopping the enumeration</param>
  public ParallelTaskOptions(bool              unordered,
                             int               parallelismLimit,
                             CancellationToken cancellationToken = default)
  {
    Unordered         = unordered;
    ParallelismLimit  = parallelismLimit;
    CancellationToken = cancellationToken;
  }

  /// <summary>
  ///   Options for ParallelSelect and ParallelWait.
  ///   Parallelism is limited to the number of threads.
  /// </summary>
  /// <param name="unordered">If true, the result order does not respect input order</param>
  /// <param name="cancellationToken">Cancellation token used for stopping the enumeration</param>
  public ParallelTaskOptions(bool              unordered,
                             CancellationToken cancellationToken = default)
    : this(unordered,
           0,
           cancellationToken)
  {
  }
}

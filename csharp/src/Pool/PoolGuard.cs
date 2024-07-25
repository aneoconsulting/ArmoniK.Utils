// This file is part of the ArmoniK project
//
// Copyright (C) ANEO, 2022-2024. All rights reserved.
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
using System.Threading.Tasks;

using JetBrains.Annotations;

namespace ArmoniK.Utils.Pool;

/// <summary>
///   Class managing the acquire and release of an object from the pool.
///   The object is acquired when the guard is created, and released when the guard is disposed.
/// </summary>
public sealed class PoolGuard<T> : IDisposable, IAsyncDisposable
{
  private Func<Exception?, CancellationToken, ValueTask>? release_;

  internal PoolGuard(T                                              obj,
                     Func<Exception?, CancellationToken, ValueTask> release,
                     CancellationToken                              releaseCancellationToken)
  {
    release_                 = release;
    Value                    = obj;
    ReleaseCancellationToken = releaseCancellationToken;
  }

  /// <summary>
  ///   Acquired object
  /// </summary>
  [PublicAPI]
  public T Value { get; }

  /// <summary>
  ///   Cancellation token used upon release
  /// </summary>
  [PublicAPI]
  public CancellationToken ReleaseCancellationToken { get; set; }

  /// <summary>
  ///   Exception that occurred while using the guard
  /// </summary>
  [PublicAPI]
  public Exception? Exception { get; set; }

  /// <inheritdoc />
  public async ValueTask DisposeAsync()
  {
    var release = Interlocked.Exchange(ref release_,
                                       null);
    if (release is not null)
    {
      await release(Exception,
                    ReleaseCancellationToken)
        .ConfigureAwait(false);
      GC.SuppressFinalize(this);
    }
  }

  /// <inheritdoc />
  public void Dispose()
    => DisposeAsync()
      .WaitSync();

  /// <summary>
  ///   Finalizer
  /// </summary>
  ~PoolGuard()
  {
    try
    {
      Dispose();
    }
    catch (ObjectDisposedException)
    {
      // In case both the guard and the pool was not disposed, and are collected at the same time,
      // the inner bag and semaphore of the pool might also be collected at the same time, and already finalized.
      // Therefore, we could have a Dispose exception when Releasing the guarded object.
    }
  }

  /// <summary>
  ///   Get the acquired object
  /// </summary>
  /// <param name="guard">Guard to get the object from</param>
  /// <returns>The acquired object</returns>
  [PublicAPI]
  public static implicit operator T(PoolGuard<T> guard)
    => guard.Value;
}

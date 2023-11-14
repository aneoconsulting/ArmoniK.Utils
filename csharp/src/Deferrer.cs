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

using System;
using System.Threading;
using System.Threading.Tasks;

using JetBrains.Annotations;

namespace ArmoniK.Utils;

/// <summary>
///   Wraps an action that will be called when the object is disposed
/// </summary>
public sealed class Deferrer : IDisposable, IAsyncDisposable
{
  /// <summary>
  ///   A Disposable object that does nothing
  /// </summary>
  [PublicAPI]
  public static readonly Deferrer Empty = new();

  private object? deferred_;

  /// <summary>
  ///   Constructs a Disposable object that does nothing
  /// </summary>
  [PublicAPI]
  public Deferrer()
  {
  }

  /// <summary>
  ///   Constructs a Disposable object that calls <paramref name="deferred" /> when disposed
  /// </summary>
  /// <param name="deferred">Action to be called at Dispose</param>
  [PublicAPI]
  public Deferrer(Action? deferred)
    => deferred_ = deferred;

  /// <summary>
  ///   Constructs a Disposable object that calls <paramref name="asyncDeferred" /> when disposed
  /// </summary>
  /// <param name="asyncDeferred">Function to be called at Dispose</param>
  [PublicAPI]
  public Deferrer(Func<ValueTask>? asyncDeferred)
    => deferred_ = asyncDeferred;

  /// <summary>
  ///   Constructs a Disposable object that disposes <paramref name="disposable" /> when disposed
  /// </summary>
  /// <param name="disposable">Action to be called at Dispose</param>
  [PublicAPI]
  public Deferrer(IDisposable? disposable)
    => deferred_ = disposable;

  /// <summary>
  ///   Constructs a Disposable object that disposes <paramref name="asyncDisposable" /> when disposed
  /// </summary>
  /// <param name="asyncDisposable">Action to be called at Dispose</param>
  [PublicAPI]
  public Deferrer(IAsyncDisposable? asyncDisposable)
    => deferred_ = asyncDisposable;

  /// <inheritdoc />
  public async ValueTask DisposeAsync()
  {
    // Beware of race conditions:
    // https://learn.microsoft.com/en-us/dotnet/standard/security/security-and-race-conditions#race-conditions-in-the-dispose-method
    var deferred = Interlocked.Exchange(ref deferred_,
                                        null);

    switch (deferred)
    {
      // Check asynchronous first
      case Func<ValueTask> asyncF:
        await asyncF()
          .ConfigureAwait(false);
        break;
      case Action f:
        f();
        break;
      // As Func and Action are sealed, it is not possible to be both Action and Disposable
      case IAsyncDisposable asyncDisposable:
        await asyncDisposable.DisposeAsync()
                             .ConfigureAwait(false);
        break;
      case IDisposable disposable:
        disposable.Dispose();
        break;
    }

    GC.SuppressFinalize(this);
  }

  /// <inheritdoc />
  public void Dispose()
  {
    // Beware of race conditions:
    // https://learn.microsoft.com/en-us/dotnet/standard/security/security-and-race-conditions#race-conditions-in-the-dispose-method
    var deferred = Interlocked.Exchange(ref deferred_,
                                        null);
    switch (deferred)
    {
      // Check synchronous first
      case Action f:
        f();
        break;
      case Func<ValueTask> asyncF:
        asyncF()
          .WaitSync();
        break;
      // As Func and Action are sealed, it is not possible to be both Action and Disposable
      case IDisposable disposable:
        disposable.Dispose();
        break;
      case IAsyncDisposable asyncDisposable:
        asyncDisposable.DisposeAsync()
                       .WaitSync();
        break;
    }

    GC.SuppressFinalize(this);
  }

  /// <summary>
  ///   Reset the Disposable to does nothing when disposed.
  ///   The previous action will not be called.
  /// </summary>
  [PublicAPI]
  public void Reset()
    => deferred_ = null;

  /// <summary>
  ///   Reset the Disposable to calls <paramref name="deferred" /> when disposed.
  ///   The previous action will not be called.
  /// </summary>
  /// <param name="deferred">Action to be called at Dispose</param>
  [PublicAPI]
  public void Reset(Action? deferred)
    => deferred_ = deferred;

  /// <summary>
  ///   Reset the Disposable to calls <paramref name="asyncDeferred" /> when disposed.
  ///   The previous action will not be called.
  /// </summary>
  /// <param name="asyncDeferred">Function to be called at Dispose</param>
  [PublicAPI]
  public void Reset(Func<ValueTask>? asyncDeferred)
    => deferred_ = asyncDeferred;

  /// <summary>
  ///   Reset the Disposable to disposes <paramref name="disposable" /> when disposed.
  ///   The previous action will not be called.
  /// </summary>
  /// <param name="disposable">Action to be called at Dispose</param>
  [PublicAPI]
  public void Reset(IDisposable? disposable)
    => deferred_ = disposable;

  /// <summary>
  ///   Reset the Disposable to disposes <paramref name="asyncDisposable" /> when disposed.
  ///   The previous action will not be called.
  /// </summary>
  /// <param name="asyncDisposable">Action to be called at Dispose</param>
  [PublicAPI]
  public void Reset(IAsyncDisposable? asyncDisposable)
    => deferred_ = asyncDisposable;

  ~Deferrer()
    => Dispose();
}

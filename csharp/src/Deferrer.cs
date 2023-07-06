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

  private Func<ValueTask>? asyncDeferred_;

  private Action? deferred_;

  /// <summary>
  ///   Constructs a Disposable object that calls <paramref name="deferred" /> when disposed
  /// </summary>
  /// <param name="deferred">Action to be called at Dispose</param>
  [PublicAPI]
  public Deferrer(Action deferred)
    => deferred_ = deferred;

  /// <summary>
  ///   Constructs a Disposable object that calls <paramref name="deferred" /> when disposed
  /// </summary>
  /// <param name="deferred">Function to be called at Dispose</param>
  [PublicAPI]
  public Deferrer(Func<ValueTask> deferred)
    => asyncDeferred_ = deferred;

  private Deferrer()
  {
  }

  /// <inheritdoc />
  public ValueTask DisposeAsync()
  {
    // Check asynchronous action first
    if (asyncDeferred_ is not null)
    {
      return DisposeAsyncCore();
    }

    if (deferred_ is not null)
    {
      DisposeCore();
    }

    return new ValueTask();
  }

  /// <inheritdoc />
  public void Dispose()
  {
    // Check synchronous action first
    if (deferred_ is not null)
    {
      DisposeCore();
    }
    else if (asyncDeferred_ is not null)
    {
      DisposeAsyncCore()
        .WaitSync();
    }
  }

  // Dispose using the synchronous function
  private void DisposeCore()
  {
    // Beware of race conditions:
    // https://learn.microsoft.com/en-us/dotnet/standard/security/security-and-race-conditions#race-conditions-in-the-dispose-method
    var deferred = Interlocked.Exchange(ref deferred_,
                                        null);

    if (deferred is null)
    {
      return;
    }

    deferred();
    GC.SuppressFinalize(this);
  }

  // Dispose using the asynchronous function
  private async ValueTask DisposeAsyncCore()
  {
    // Beware of race conditions:
    // https://learn.microsoft.com/en-us/dotnet/standard/security/security-and-race-conditions#race-conditions-in-the-dispose-method
    var asyncDeferred = Interlocked.Exchange(ref asyncDeferred_,
                                             null);

    if (asyncDeferred is null)
    {
      return;
    }

    await asyncDeferred();

    GC.SuppressFinalize(this);
  }

  ~Deferrer()
    => Dispose();
}

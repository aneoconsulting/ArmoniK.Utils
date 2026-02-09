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
using System.Threading.Tasks;

namespace ArmoniK.Utils.Pool;

/// <summary>
///   Manages the lifetime of an object using a reference counter.
///   The object is disposed as soon as its last <see cref="RefCounted" /> reference is disposed.
/// </summary>
internal sealed class RefCounted : IAsyncDisposable, IDisposable
{
  private readonly object? value_;
  private          int     refCount_;

  /// <summary>
  ///   Create a new <see cref="RefCounted" />.
  /// </summary>
  /// <param name="value">value</param>
  internal RefCounted(object? value)
    => value_ = value;


  /// <inheritdoc />
  public ValueTask DisposeAsync()
  {
    switch (value_)
    {
      case IAsyncDisposable asyncDisposable:
        return asyncDisposable.DisposeAsync();
      case IDisposable disposable:
        disposable.Dispose();
        break;
    }

    return new ValueTask();
  }

  /// <inheritdoc />
  public void Dispose()
  {
    switch (value_)
    {
      case IDisposable disposable:
        disposable.Dispose();
        break;
      case IAsyncDisposable asyncDisposable:
        asyncDisposable.DisposeAsync()
                       .WaitSync();
        break;
    }
  }

  /// <summary>
  ///   Gets a new <see cref="RefCounted" /> by incrementing the reference counter
  /// </summary>
  /// <returns>This</returns>
  internal RefCounted AcquireRef()
  {
    Interlocked.Increment(ref refCount_);
    return this;
  }

  /// <summary>
  ///   Decrements the reference counter
  /// </summary>
  /// <returns>This or null if counter is zero</returns>
  internal RefCounted? ReleaseRef()
    => Interlocked.Decrement(ref refCount_) == 0
         ? this
         : null;
}

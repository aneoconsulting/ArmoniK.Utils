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
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

using JetBrains.Annotations;

namespace ArmoniK.Utils;

/// <summary>
///   Provides support for lazy asynchronous initialization of an object.
///   This is a thin wrapper over `Lazy<Task<T>>` that can be awaited directly.
/// </summary>
/// <typeparam name="T">Type of the lazy value</typeparam>
[PublicAPI]
public class AsyncLazy<T> : Lazy<Task<T>>
{
  /// <summary>
  ///   Constructs an AsyncLazy from a synchronous factory
  /// </summary>
  /// <param name="valueFactory">Function that creates the value</param>
  [PublicAPI]
  public AsyncLazy(Func<T> valueFactory)
    : base(() => Task.Run(valueFactory))
  {
  }

  /// <summary>
  ///   Constructs an AsyncLazy from an asynchronous factory
  /// </summary>
  /// <param name="valueFactory">Asynchronous Function that creates the value</param>
  [PublicAPI]
  public AsyncLazy(Func<Task<T>> valueFactory)
    : base(valueFactory)
  {
  }

  /// <summary>
  ///   Gets an awaiter used to await this async lazy
  /// </summary>
  /// <returns>Awaiter instance</returns>
  [PublicAPI]
  public TaskAwaiter<T> GetAwaiter()
    => Value.GetAwaiter();
}

/// <summary>
///   Provides support for lazy asynchronous initialization of an external object.
///   This is a thin wrapper over `Lazy<Task>` that can be awaited directly.
/// </summary>
[PublicAPI]
public class AsyncLazy : Lazy<Task>
{
  /// <summary>
  ///   Constructs an AsyncLazy from a synchronous factory
  /// </summary>
  /// <param name="valueFactory">Function that creates the value</param>
  [PublicAPI]
  public AsyncLazy(Action valueFactory)
    : base(() => Task.Run(valueFactory))
  {
  }

  /// <summary>
  ///   Constructs an AsyncLazy from an asynchronous factory
  /// </summary>
  /// <param name="valueFactory">Asynchronous Function that creates the value</param>
  [PublicAPI]
  public AsyncLazy(Func<Task> valueFactory)
    : base(valueFactory)
  {
  }

  /// <summary>
  ///   Gets an awaiter used to await this async lazy
  /// </summary>
  /// <returns>Awaiter instance</returns>
  [PublicAPI]
  public TaskAwaiter GetAwaiter()
    => Value.GetAwaiter();
}

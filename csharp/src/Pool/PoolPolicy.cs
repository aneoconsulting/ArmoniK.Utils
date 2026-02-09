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

using JetBrains.Annotations;

namespace ArmoniK.Utils.Pool;

/// <summary>
///   Describes the behavior of the standard object pool
/// </summary>
/// <typeparam name="T">Type of the objects stored within the pool</typeparam>
public sealed class PoolPolicy<T>
{
  private object? create_;
  private object? validateAcquire_;
  private object? validateRelease_;

  /// <summary>
  ///   Limit the number of objects that can be in used at the same time.
  /// </summary>
  /// <remarks>
  ///   If it is negative, no limit is enforced.
  /// </remarks>
  [PublicAPI]
  public int MaxNumberOfInstances { get; set; } = -1;

  /// <summary>
  ///   Create a new instance for the ObjectPool
  /// </summary>
  /// <param name="cancellationToken">Request cancellation of the function</param>
  /// <returns>New instance</returns>
  [PublicAPI]
  public ValueTask<T> CreateAsync(CancellationToken cancellationToken = default)
  {
    try
    {
      return create_ switch
             {
               Func<T> f => new ValueTask<T>(f()),
               Func<ValueTask<T>> f => f(),
               Func<CancellationToken, ValueTask<T>> f => f(cancellationToken),
               _ => throw new NotSupportedException($"Could not create object of type {typeof(T)} with factory of type {create_?.GetType()}"),
             };
    }
    catch (OperationCanceledException exception)
    {
      return ValueTaskExt.FromCanceled<T>(exception.CancellationToken);
    }
    catch (Exception exception)
    {
      return ValueTaskExt.FromException<T>(exception);
    }
  }

  /// <summary>
  ///   Validate the instance <paramref name="obj" /> to know if it can be used when it is acquired from the pool.
  /// </summary>
  /// <param name="obj">Instance to validate</param>
  /// <param name="cancellationToken">Request cancellation of the function</param>
  /// <returns>Whether <paramref name="obj" /> is valid</returns>
  [PublicAPI]
  public ValueTask<bool> ValidateAcquireAsync(T                 obj,
                                              CancellationToken cancellationToken = default)
    => ValidateAsync(validateAcquire_,
                     obj,
                     null,
                     cancellationToken);

  /// <summary>
  ///   Validate the instance <paramref name="obj" /> to know if it can be used when it is release to the pool.
  /// </summary>
  /// <param name="obj">Instance to validate</param>
  /// <param name="e">Exception that has been seen while using <paramref name="obj" /></param>
  /// <param name="cancellationToken">Request cancellation of the function</param>
  /// <returns>Whether <paramref name="obj" /> is valid</returns>
  [PublicAPI]
  public ValueTask<bool> ValidateReleaseAsync(T                 obj,
                                              Exception?        e,
                                              CancellationToken cancellationToken = default)
    => ValidateAsync(validateRelease_,
                     obj,
                     e,
                     cancellationToken);

  /// <summary>
  ///   Validate the instance <paramref name="obj" /> to know if it can be used when it is release to the pool.
  /// </summary>
  /// <param name="validate">Function used to validate <paramref name="obj" /></param>
  /// <param name="obj">Instance to validate</param>
  /// <param name="e">Exception that has been seen while using <paramref name="obj" /></param>
  /// <param name="cancellationToken">Request cancellation of the function</param>
  /// <returns>Whether <paramref name="obj" /> is valid</returns>
  private static ValueTask<bool> ValidateAsync(object?           validate,
                                               T                 obj,
                                               Exception?        e,
                                               CancellationToken cancellationToken)
  {
    try
    {
      return validate switch
             {
               null            => new ValueTask<bool>(true),
               Func<T, bool> f => new ValueTask<bool>(f(obj)),
               Func<T, Exception?, bool> f => new ValueTask<bool>(f(obj,
                                                                    e)),
               Func<T, ValueTask<bool>> f => f(obj),
               Func<T, CancellationToken, ValueTask<bool>> f => f(obj,
                                                                  cancellationToken),
               Func<T, Exception?, ValueTask<bool>> f => f(obj,
                                                           e),
               Func<T, Exception?, CancellationToken, ValueTask<bool>> f => f(obj,
                                                                              e,
                                                                              cancellationToken),
               _ => throw new NotSupportedException($"Could not validate pooled object of type {typeof(T)} with validator of type {validate.GetType()}"),
             };
    }
    catch (OperationCanceledException exception)
    {
      return ValueTaskExt.FromCanceled<bool>(exception.CancellationToken);
    }
    catch (Exception exception)
    {
      return ValueTaskExt.FromException<bool>(exception);
    }
  }

  /// <summary>
  ///   Set the maximum number of instances that can be in use at the same time
  /// </summary>
  /// <param name="n">maximum number of instances</param>
  /// <returns>This</returns>
  [PublicAPI]
  public PoolPolicy<T> SetMaxNumberOfInstances(int n)
  {
    MaxNumberOfInstances = n;
    return this;
  }

  /// <summary>
  ///   Set the function to create new instances
  /// </summary>
  /// <param name="create">Function that create a new instance</param>
  /// <returns>This</returns>
  private PoolPolicy<T> SetCreateInternal(object create)
  {
    create_ = create;
    return this;
  }

  /// <summary>
  ///   Set the function to validate instances when acquired
  /// </summary>
  /// <param name="validate">Function used to validate an instance</param>
  /// <returns>This</returns>
  private PoolPolicy<T> SetValidateAcquireInternal(object? validate)
  {
    validateAcquire_ = validate;
    return this;
  }

  /// <summary>
  ///   Set the function to validate instances when released
  /// </summary>
  /// <param name="validate">Function used to validate an instance</param>
  /// <returns>This</returns>
  private PoolPolicy<T> SetValidateReleaseInternal(object? validate)
  {
    validateRelease_ = validate;
    return this;
  }

  /// <summary>
  ///   Set the function to validate instances when acquired *and* released
  /// </summary>
  /// <param name="validate">Function used to validate an instance</param>
  /// <returns>This</returns>
  private PoolPolicy<T> SetValidateInternal(object? validate)
    => SetValidateAcquireInternal(validate)
      .SetValidateReleaseInternal(validate);

  /// <inheritdoc cref="SetCreateInternal" />
  [PublicAPI]
  public PoolPolicy<T> SetCreate(Func<T> create)
    => SetCreateInternal(create);

  /// <inheritdoc cref="SetCreateInternal" />
  [PublicAPI]
  public PoolPolicy<T> SetCreate(Func<ValueTask<T>> create)
    => SetCreateInternal(create);

  /// <inheritdoc cref="SetCreateInternal" />
  [PublicAPI]
  public PoolPolicy<T> SetCreate(Func<CancellationToken, ValueTask<T>> create)
    => SetCreateInternal(create);

  /// <inheritdoc cref="SetValidateAcquireInternal" />
  [PublicAPI]
  public PoolPolicy<T> SetValidateAcquire(Func<T, bool>? validate)
    => SetValidateAcquireInternal(validate);

  /// <inheritdoc cref="SetValidateAcquireInternal" />
  [PublicAPI]
  public PoolPolicy<T> SetValidateAcquire(Func<T, Exception?, bool>? validate)
    => SetValidateAcquireInternal(validate);

  /// <inheritdoc cref="SetValidateAcquireInternal" />
  [PublicAPI]
  public PoolPolicy<T> SetValidateAcquire(Func<T, ValueTask<bool>>? validate)
    => SetValidateAcquireInternal(validate);

  /// <inheritdoc cref="SetValidateAcquireInternal" />
  [PublicAPI]
  public PoolPolicy<T> SetValidateAcquire(Func<T, CancellationToken, ValueTask<bool>>? validate)
    => SetValidateAcquireInternal(validate);

  /// <inheritdoc cref="SetValidateAcquireInternal" />
  [PublicAPI]
  public PoolPolicy<T> SetValidateAcquire(Func<T, Exception?, ValueTask<bool>>? validate)
    => SetValidateAcquireInternal(validate);

  /// <inheritdoc cref="SetValidateAcquireInternal" />
  [PublicAPI]
  public PoolPolicy<T> SetValidateAcquire(Func<T, Exception?, CancellationToken, ValueTask<bool>>? validate)
    => SetValidateAcquireInternal(validate);

  /// <inheritdoc cref="SetValidateReleaseInternal" />
  [PublicAPI]
  public PoolPolicy<T> SetValidateRelease(Func<T, bool>? validate)
    => SetValidateReleaseInternal(validate);

  /// <inheritdoc cref="SetValidateReleaseInternal" />
  [PublicAPI]
  public PoolPolicy<T> SetValidateRelease(Func<T, Exception?, bool>? validate)
    => SetValidateReleaseInternal(validate);

  /// <inheritdoc cref="SetValidateReleaseInternal" />
  [PublicAPI]
  public PoolPolicy<T> SetValidateRelease(Func<T, ValueTask<bool>>? validate)
    => SetValidateReleaseInternal(validate);

  /// <inheritdoc cref="SetValidateReleaseInternal" />
  [PublicAPI]
  public PoolPolicy<T> SetValidateRelease(Func<T, CancellationToken, ValueTask<bool>>? validate)
    => SetValidateReleaseInternal(validate);

  /// <inheritdoc cref="SetValidateReleaseInternal" />
  [PublicAPI]
  public PoolPolicy<T> SetValidateRelease(Func<T, Exception?, ValueTask<bool>>? validate)
    => SetValidateReleaseInternal(validate);

  /// <inheritdoc cref="SetValidateReleaseInternal" />
  [PublicAPI]
  public PoolPolicy<T> SetValidateRelease(Func<T, Exception?, CancellationToken, ValueTask<bool>>? validate)
    => SetValidateReleaseInternal(validate);

  /// <inheritdoc cref="SetValidateInternal" />
  [PublicAPI]
  public PoolPolicy<T> SetValidate(Func<T, bool>? validate)
    => SetValidateInternal(validate);

  /// <inheritdoc cref="SetValidateInternal" />
  [PublicAPI]
  public PoolPolicy<T> SetValidate(Func<T, Exception?, bool>? validate)
    => SetValidateInternal(validate);

  /// <inheritdoc cref="SetValidateInternal" />
  [PublicAPI]
  public PoolPolicy<T> SetValidate(Func<T, ValueTask<bool>>? validate)
    => SetValidateInternal(validate);

  /// <inheritdoc cref="SetValidateInternal" />
  [PublicAPI]
  public PoolPolicy<T> SetValidate(Func<T, CancellationToken, ValueTask<bool>>? validate)
    => SetValidateInternal(validate);

  /// <inheritdoc cref="SetValidateInternal" />
  [PublicAPI]
  public PoolPolicy<T> SetValidate(Func<T, Exception?, ValueTask<bool>>? validate)
    => SetValidateInternal(validate);

  /// <inheritdoc cref="SetValidateInternal" />
  [PublicAPI]
  public PoolPolicy<T> SetValidate(Func<T, Exception?, CancellationToken, ValueTask<bool>>? validate)
    => SetValidateInternal(validate);
}

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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ArmoniK.Utils.Pool;

/// <summary>
///   Class to manage a plain pool of objects of type <typeparamref name="T" />
/// </summary>
internal sealed class PoolInternal<T> : IRefDisposable
{
  private readonly ConcurrentBag<T>                             bag_;
  private readonly Func<CancellationToken, ValueTask<T>>        createFunc_;
  private readonly SemaphoreSlim?                               sem_;
  private readonly Func<T, CancellationToken, ValueTask<bool>>? validateFunc_;

  private int refCount_;

  /// <summary>
  ///   Create a new ObjectPool that can have at most <paramref name="max" /> objects created at the same time.
  /// </summary>
  /// <remarks>
  ///   If <paramref name="max" /> is negative, no limit is enforced.
  /// </remarks>
  /// <param name="max">Maximum number of objects</param>
  /// <param name="createFunc">Function to call to create new objects</param>
  /// <param name="validateFunc">Function to call to check if an object is still valid and can be safely reused</param>
  internal PoolInternal(int                                          max,
                        Func<CancellationToken, ValueTask<T>>        createFunc,
                        Func<T, CancellationToken, ValueTask<bool>>? validateFunc = null)
  {
    // For now, the object pool is constructed directly with 2 functions, but in the future
    // we might want to use a `ObjectPoolPolicy` instead, and make this constructor
    // just build a standard `ObjectPoolPolicy` from the functions.

    sem_ = max switch
           {
             < 0 => null,
             0   => throw new ArgumentOutOfRangeException($"{nameof(max)} cannot be zero"),
             _   => new SemaphoreSlim(max),
           };
    bag_          = new ConcurrentBag<T>();
    createFunc_   = createFunc;
    validateFunc_ = validateFunc;
  }

  /// <inheritdoc />
  public async ValueTask DisposeAsync()
  {
    var errors = new List<Exception>();

    foreach (var obj in bag_)
    {
      try
      {
        await DisposeOneAsync(obj)
          .ConfigureAwait(false);
      }
      catch (Exception error)
      {
        errors.Add(error);
      }
    }

    sem_?.Dispose();

    if (errors.Any())
    {
      throw new AggregateException(errors);
    }
  }

  /// <inheritdoc />
  public void Dispose()
  {
    var errors = new List<Exception>();

    foreach (var obj in bag_)
    {
      try
      {
        DisposeOne(obj);
      }
      catch (Exception error)
      {
        errors.Add(error);
      }
    }

    sem_?.Dispose();

    if (errors.Any())
    {
      throw new AggregateException(errors);
    }
  }

  public IRefDisposable AcquireRef()
  {
    Interlocked.Increment(ref refCount_);
    return this;
  }

  public IRefDisposable? ReleaseRef()
    => Interlocked.Decrement(ref refCount_) == 0
         ? this
         : null;

  // An object pool has only references to managed objects.
  // So finalizer is not required as it will be called directly by the underlying resources

  /// <summary>
  ///   Acquire a new object from the pool, creating it if there is no object in the pool.
  ///   If the limit of object has been reached, this method will wait for an object to be released.
  /// </summary>
  /// <remarks>
  ///   This method has been marked private to avoid missing the call to <see cref="Release" />
  /// </remarks>
  /// <param name="cancellationToken">Cancellation token used for stopping the acquire</param>
  /// <exception cref="OperationCanceledException">Exception thrown when the cancellation is requested</exception>
  /// <returns>An object that has been acquired</returns>
  internal async ValueTask<T> Acquire(CancellationToken cancellationToken = default)
  {
    if (sem_ is not null)
    {
      await sem_.WaitAsync(cancellationToken)
                .ConfigureAwait(false);
    }

    try
    {
      while (bag_.TryTake(out var res))
      {
        var isValid = validateFunc_ is null || await validateFunc_(res,
                                                                   cancellationToken)
                        .ConfigureAwait(false);

        if (isValid)
        {
          return res;
        }

        await DisposeOneAsync(res)
          .ConfigureAwait(false);
      }

      return await createFunc_(cancellationToken)
               .ConfigureAwait(false);
    }
    catch
    {
      sem_?.Release();
      throw;
    }
  }

  /// <summary>
  ///   Release an object to the pool. If the object is still valid, another consumer can reuse the object.
  /// </summary>
  /// <remarks>
  ///   This method has been marked private to avoid missing to call it.
  /// </remarks>
  /// <param name="obj">Object to release to the pool</param>
  /// <param name="cancellationToken">Cancellation token used for stopping the release</param>
  /// <exception cref="OperationCanceledException">Exception thrown when the cancellation is requested</exception>
  internal async ValueTask Release(T                 obj,
                                   CancellationToken cancellationToken = default)
  {
    try
    {
      var isValid = validateFunc_ is null || await validateFunc_(obj,
                                                                 cancellationToken)
                      .ConfigureAwait(false);

      if (isValid)
      {
        bag_.Add(obj);
      }
      else
      {
        await DisposeOneAsync(obj)
          .ConfigureAwait(false);
      }
    }
    finally
    {
      sem_?.Release();
    }
  }

  /// <summary>
  ///   Dispose a single object, if it is disposable
  /// </summary>
  /// <param name="obj">Object to dispose</param>
  private static void DisposeOne(T obj)
  {
    switch (obj)
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
  ///   Asynchronously Dispose a single object, if it is disposable
  /// </summary>
  /// <param name="obj">Object to dispose</param>
  private static async ValueTask DisposeOneAsync(T obj)
  {
    switch (obj)
    {
      case IAsyncDisposable asyncDisposable:
        await asyncDisposable.DisposeAsync()
                             .ConfigureAwait(false);
        break;
      case IDisposable disposable:
        disposable.Dispose();
        break;
    }
  }
}

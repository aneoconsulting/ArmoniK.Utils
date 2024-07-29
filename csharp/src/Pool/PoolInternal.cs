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
  private readonly ConcurrentBag<T> bag_;
  private readonly PoolPolicy<T>    policy_;
  private readonly SemaphoreSlim?   sem_;

  private int refCount_;

  /// <summary>
  ///   Create a new PoolInternal using the poolPolicy.
  /// </summary>
  /// <param name="policy">How the pool is configured</param>
  internal PoolInternal(PoolPolicy<T> policy)
  {
    sem_ = policy.MaxNumberOfInstances switch
           {
             < 0   => null,
             0     => throw new ArgumentOutOfRangeException($"{nameof(policy.MaxNumberOfInstances)} cannot be zero"),
             var n => new SemaphoreSlim(n),
           };
    bag_    = new ConcurrentBag<T>();
    policy_ = policy;
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

  IRefDisposable IRefDisposable.AcquireRef()
  {
    Interlocked.Increment(ref refCount_);
    return this;
  }

  IRefDisposable? IRefDisposable.ReleaseRef()
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
        var isValid = await policy_.ValidateAcquireAsync(res,
                                                         cancellationToken)
                                   .ConfigureAwait(false);

        if (isValid)
        {
          return res;
        }

        await DisposeOneAsync(res)
          .ConfigureAwait(false);
      }

      return await policy_.CreateAsync(cancellationToken)
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
  /// <param name="e">Exception that has been seen during the use of obj</param>
  /// <param name="cancellationToken">Cancellation token used for stopping the release</param>
  /// <exception cref="OperationCanceledException">Exception thrown when the cancellation is requested</exception>
  internal async ValueTask Release(T                 obj,
                                   Exception?        e,
                                   CancellationToken cancellationToken)
  {
    try
    {
      var isValid = await policy_.ValidateReleaseAsync(obj,
                                                       e,
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
  private static ValueTask DisposeOneAsync(T obj)
  {
    switch (obj)
    {
      case IAsyncDisposable asyncDisposable:
        return asyncDisposable.DisposeAsync();
      case IDisposable disposable:
        disposable.Dispose();
        break;
    }

    return new ValueTask();
  }
}

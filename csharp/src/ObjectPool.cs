// This file is part of the ArmoniK project
//
// Copyright (C) ANEO, 2022-2024.All rights reserved.
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

using JetBrains.Annotations;

namespace ArmoniK.Utils;

/// <summary>
///   Class to manage a pool of objects of type <typeparamref name="T" />
/// </summary>
/// <remarks>
///   <para>
///     An ObjectPool is useful when you want to reuse objects that are not used anymore.
///     This object pool can be configured to have a limit on the number of objects created at the same time.
///   </para>
///   <para>
///     When the pool is empty, objects are created from a function specified at the construction of the pool.
///     Created objects can then be reused once they are released.
///   </para>
///   <para>
///     When an object is released, a "validate function" is called to check if the object is still valid.
///     If the object is still valid, it is returned to the pool and could be reused later on.
///     If the object is not valid, it is disposed (if disposable) and <i>not</i> returned to the pool.
///   </para>
///   <para>
///     Validity of the pooled objects is also checked when an object is acquired again.
///     Therefore, if the validity of the object expires after it has been released to the pool,
///     it is still guaranteed to get back valid objects from the pool.
///   </para>
///   <para>
///     When the pool is disposed, all objects in the pool are disposed (if disposable).
///   </para>
///   <para>
///     All objects must be released to the pool before disposing the pool.
///   </para>
/// </remarks>
/// <example>
///   <para>
///     <code>
///       // Create a client synchronously and limit the pool to 10 clients.
///       // Acquire an object and use it in a callback.
///       using var pool = new ObjectPool&lt;Client&gt;(10,
///                                               () => new Client());
///       /* ... */
///       var result = pool.WithInstance(client => client.DoSomething());
///     </code>
///   </para>
///   <para>
///     <code>
///       // Create a client <i>asynchronously</i> without object limit.
///       // Verify that a client is still valid before returning it to the pool.
///       // Acquire an object with a Guard.
///       await using var pool = new ObjectPool&lt;AsyncClient&gt;(async ct => await AsyncClient.CreateAsync(ct),
///                                                          async (client, ct) => await client.PingAsync(ct));
///       /* ... */
///       await using var client = await pool.GetAsync(cancellationToken);
///
///       var result = client.Value.DoSomething();
///     </code>
///   </para>
/// </example>
/// <typeparam name="T">Type of the objects in the pool</typeparam>
public class ObjectPool<T> : IDisposable, IAsyncDisposable
{
  private readonly ConcurrentBag<T>                                        bag_;
  private readonly Func<CancellationToken, ValueTask<T>>                   createFunc_;
  private readonly Func<T, Exception, CancellationToken, ValueTask<bool>>? onErrorFunc_;
  private readonly SemaphoreSlim                                           sem_;
  private readonly Func<T, CancellationToken, ValueTask<bool>>?            validateFunc_;

  /// <summary>
  ///   Create a new ObjectPool that can have at most <paramref name="max" /> objects created at the same time.
  /// </summary>
  /// <remarks>
  ///   If <paramref name="max" /> is negative, no limit is enforced.
  /// </remarks>
  /// <param name="max">Maximum number of objects</param>
  /// <param name="createFunc">Function to call to create new objects</param>
  /// <param name="validateFunc">Function to call to check if an object is still valid and can be safely reused</param>
  /// <param name="onErrorFunc">Function to call when an exception is raised</param>
  [PublicAPI]
  public ObjectPool(int                                                     max,
                    Func<CancellationToken, ValueTask<T>>                   createFunc,
                    Func<T, CancellationToken, ValueTask<bool>>?            validateFunc = null,
                    Func<T, Exception, CancellationToken, ValueTask<bool>>? onErrorFunc  = null)
  {
    // For now, the object pool is constructed directly with 2 functions, but in the future
    // we might want to use a `ObjectPoolPolicy` instead, and make this constructor
    // just build a standard `ObjectPoolPolicy` from the functions.
    max = max switch
          {
            < 0 => int.MaxValue,
            0   => throw new ArgumentOutOfRangeException($"{nameof(max)} cannot be zero"),
            _   => max,
          };

    bag_          = new ConcurrentBag<T>();
    sem_          = new SemaphoreSlim(max);
    createFunc_   = createFunc;
    validateFunc_ = validateFunc;
    onErrorFunc_  = onErrorFunc;
  }

  /// <summary>
  ///   Create a new ObjectPool without limit on the number of objects created.
  /// </summary>
  /// <param name="createFunc">Function to call to create new objects</param>
  /// <param name="validateFunc">Function to call to check if an object is still valid and can be safely reused</param>
  /// <param name="onErrorFunc">Function to call when an exception is raised</param>
  [PublicAPI]
  public ObjectPool(Func<CancellationToken, ValueTask<T>>                   createFunc,
                    Func<T, CancellationToken, ValueTask<bool>>?            validateFunc = null,
                    Func<T, Exception, CancellationToken, ValueTask<bool>>? onErrorFunc  = null)
    : this(-1,
           createFunc,
           validateFunc,
           onErrorFunc)
  {
  }

  /// <summary>
  ///   Create a new ObjectPool that can have at most <paramref name="max" /> objects created at the same time.
  /// </summary>
  /// <remarks>
  ///   If <paramref name="max" /> is negative, no limit is enforced.
  /// </remarks>
  /// <param name="max">Maximum number of objects</param>
  /// <param name="createFunc">Function to call to create new objects</param>
  /// <param name="validateFunc">Function to call to check if an object is still valid and can be safely reused</param>
  /// <param name="onErrorFunc">Function to call when an exception is raised</param>
  [PublicAPI]
  public ObjectPool(int                       max,
                    Func<T>                   createFunc,
                    Func<T, bool>?            validateFunc = null,
                    Func<T, Exception, bool>? onErrorFunc  = null)
    : this(max,
           _ => new ValueTask<T>(createFunc()),
           validateFunc is not null
             ? (x,
                _) => new ValueTask<bool>(validateFunc(x))
             : null,
           onErrorFunc is not null
             ? (x,
                e,
                _) => new ValueTask<bool>(onErrorFunc(x,
                                                      e))
             : null)
  {
  }

  /// <summary>
  ///   Create a new ObjectPool without limit on the number of objects created.
  /// </summary>
  /// <param name="createFunc">Function to call to create new objects</param>
  /// <param name="validateFunc">Function to call to check if an object is still valid and can be safely reused</param>
  /// <param name="onErrorFunc">Function to call when an exception is raised</param>
  [PublicAPI]
  public ObjectPool(Func<T>                   createFunc,
                    Func<T, bool>?            validateFunc = null,
                    Func<T, Exception, bool>? onErrorFunc  = null)
    : this(-1,
           createFunc,
           validateFunc,
           onErrorFunc)
  {
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

    sem_.Dispose();

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

    sem_.Dispose();

    if (errors.Any())
    {
      throw new AggregateException(errors);
    }
  }

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
  private async ValueTask<T> Acquire(CancellationToken cancellationToken = default)
  {
    await sem_.WaitAsync(cancellationToken)
              .ConfigureAwait(false);

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
      sem_.Release();
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
  /// <param name="exception">Exception that occured during the lifetime of the object</param>
  /// <param name="cancellationToken">Cancellation token used for stopping the release</param>
  /// <exception cref="OperationCanceledException">Exception thrown when the cancellation is requested</exception>
  private async ValueTask Release(T                 obj,
                                  Exception?        exception         = null,
                                  CancellationToken cancellationToken = default)
  {
    try
    {
      var isValid = true;

      if (exception is not null && onErrorFunc_ is not null)
      {
        isValid = await onErrorFunc_(obj,
                                     exception,
                                     cancellationToken)
                    .ConfigureAwait(false);
      }

      if (isValid && validateFunc_ is not null)
      {
        isValid = await validateFunc_(obj,
                                      cancellationToken)
                    .ConfigureAwait(false);
      }

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
      sem_.Release();
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

  /// <summary>
  ///   Acquire a new object from the pool, creating it if there is no object in the pool.
  ///   If the limit of object has been reached, this method will wait for an object to be released.
  ///   The method returns a guard that contains the acquired object, and that automatically release the object when
  ///   disposed.
  /// </summary>
  /// <remarks>
  ///   If an exception is thrown during the creation of the object, acquire is cancelled (semaphore is released).
  /// </remarks>
  /// <param name="cancellationToken">Cancellation token used for stopping the Acquire of a new object</param>
  /// <exception cref="OperationCanceledException">Exception thrown when the cancellation is requested</exception>
  /// <returns>A guard that contains the acquired object.</returns>
  [PublicAPI]
  public ValueTask<Guard> GetAsync(CancellationToken cancellationToken = default)
    => Guard.Create(this,
                    cancellationToken);

  /// <summary>
  ///   Acquire a new object from the pool, creating it if there is no object in the pool.
  ///   If the limit of object has been reached, this method will wait for an object to be released.
  ///   The method returns a guard that contains the acquired object, and that automatically release the object when
  ///   disposed.
  /// </summary>
  /// <remarks>
  ///   If an exception is thrown during the creation of the object, acquire is cancelled (semaphore is released).
  /// </remarks>
  /// <returns>A guard that contains the acquired object.</returns>
  [PublicAPI]
  public Guard Get()
    => GetAsync()
      .WaitSync();


  /// <summary>
  ///   Acquire a new object from the pool, creating it if there is no object in the pool.
  ///   If the limit of object has been reached, this method will wait for an object to be released.
  ///   Once the object has been created, <paramref name="f" /> is called with the acquired object.
  /// </summary>
  /// <param name="f">Function to call with the acquired object</param>
  /// <param name="cancellationToken">Cancellation token used for stopping the Acquire of a new object</param>
  /// <exception cref="OperationCanceledException">Exception thrown when the cancellation is requested</exception>
  /// <typeparam name="TOut">Return type of <paramref name="f" /></typeparam>
  /// <returns>Return value of <paramref name="f" /></returns>
  [PublicAPI]
  public async ValueTask<TOut> WithInstanceAsync<TOut>(Func<T, TOut>     f,
                                                       CancellationToken cancellationToken = default)
  {
    var guard = await GetAsync(cancellationToken)
                  .ConfigureAwait(false);

    try
    {
      return f(guard.Value);
    }
    catch (Exception e)
    {
      guard.RecordException(e);

      throw;
    }
    finally
    {
      await guard.DisposeAsync()
                 .ConfigureAwait(false);
    }
  }


  /// <summary>
  ///   Acquire a new object from the pool, creating it if there is no object in the pool.
  ///   If the limit of object has been reached, this method will wait for an object to be released.
  ///   Once the object has been created, <paramref name="f" /> is called with the acquired object.
  /// </summary>
  /// <param name="f">Function to call with the acquired object</param>
  /// <param name="cancellationToken">Cancellation token used for stopping the Acquire of a new object</param>
  /// <exception cref="OperationCanceledException">Exception thrown when the cancellation is requested</exception>
  /// <returns>Task representing the completion of the call</returns>
  [PublicAPI]
  public async ValueTask WithInstanceAsync(Action<T>         f,
                                           CancellationToken cancellationToken = default)
  {
    var guard = await GetAsync(cancellationToken)
                  .ConfigureAwait(false);

    try
    {
      f(guard.Value);
    }
    catch (Exception e)
    {
      guard.RecordException(e);

      throw;
    }
    finally
    {
      await guard.DisposeAsync()
                 .ConfigureAwait(false);
    }
  }

  /// <summary>
  ///   Acquire a new object from the pool, creating it if there is no object in the pool.
  ///   If the limit of object has been reached, this method will wait for an object to be released.
  ///   Once the object has been created, <paramref name="f" /> is called with the acquired object.
  /// </summary>
  /// <param name="f">Function to call with the acquired object</param>
  /// <param name="cancellationToken">Cancellation token used for stopping the Acquire of a new object</param>
  /// <exception cref="OperationCanceledException">Exception thrown when the cancellation is requested</exception>
  /// <typeparam name="TOut">Return type of <paramref name="f" /></typeparam>
  /// <returns>Return value of <paramref name="f" /></returns>
  [PublicAPI]
  public async ValueTask<TOut> WithInstanceAsync<TOut>(Func<T, ValueTask<TOut>> f,
                                                       CancellationToken        cancellationToken = default)
  {
    var guard = await GetAsync(cancellationToken)
                  .ConfigureAwait(false);

    try
    {
      return await f(guard.Value)
               .ConfigureAwait(false);
    }
    catch (Exception e)
    {
      guard.RecordException(e);

      throw;
    }
    finally
    {
      await guard.DisposeAsync()
                 .ConfigureAwait(false);
    }
  }

  /// <summary>
  ///   Acquire a new object from the pool, creating it if there is no object in the pool.
  ///   If the limit of object has been reached, this method will wait for an object to be released.
  ///   Once the object has been created, <paramref name="f" /> is called with the acquired object.
  /// </summary>
  /// <param name="f">Function to call with the acquired object</param>
  /// <param name="cancellationToken">Cancellation token used for stopping the Acquire of a new object</param>
  /// <exception cref="OperationCanceledException">Exception thrown when the cancellation is requested</exception>
  /// <returns>Task representing the completion of the call</returns>
  [PublicAPI]
  public async ValueTask WithInstanceAsync(Func<T, ValueTask> f,
                                           CancellationToken  cancellationToken = default)
  {
    var guard = await GetAsync(cancellationToken)
                  .ConfigureAwait(false);

    try
    {
      await f(guard.Value)
        .ConfigureAwait(false);
    }
    catch (Exception e)
    {
      guard.RecordException(e);

      throw;
    }
    finally
    {
      await guard.DisposeAsync()
                 .ConfigureAwait(false);
    }
  }


  /// <summary>
  ///   Acquire a new object from the pool, creating it if there is no object in the pool.
  ///   If the limit of object has been reached, this method will wait for an object to be released.
  ///   Once the object has been created, <paramref name="f" /> is called with the acquired object.
  /// </summary>
  /// <param name="f">Function to call with the acquired object</param>
  /// <typeparam name="TOut">Return type of <paramref name="f" /></typeparam>
  /// <returns>Return value of <paramref name="f" /></returns>
  [PublicAPI]
  public TOut WithInstance<TOut>(Func<T, TOut> f)
  {
    var guard = Get();

    try
    {
      return f(guard.Value);
    }
    catch (Exception e)
    {
      guard.RecordException(e);

      throw;
    }
    finally
    {
      guard.Dispose();
    }
  }


  /// <summary>
  ///   Acquire a new object from the pool, creating it if there is no object in the pool.
  ///   If the limit of object has been reached, this method will wait for an object to be released.
  ///   Once the object has been created, <paramref name="f" /> is called with the acquired object.
  /// </summary>
  /// <param name="f">Function to call with the acquired object</param>
  /// <returns>Task representing the completion of the call</returns>
  [PublicAPI]
  public void WithInstance(Action<T> f)
  {
    var guard = Get();

    try
    {
      f(guard.Value);
    }
    catch (Exception e)
    {
      guard.RecordException(e);

      throw;
    }
    finally
    {
      guard.Dispose();
    }
  }

  /// <summary>
  ///   Class managing the acquire and release of an object from the pool.
  ///   The object is acquired when the guard is created, and released when the guard is disposed.
  /// </summary>
  public sealed class Guard : IDisposable, IAsyncDisposable
  {
    private Exception?     exception_;
    private ObjectPool<T>? pool_;

    private Guard(ObjectPool<T>     pool,
                  T                 obj,
                  CancellationToken releaseCancellationToken)
    {
      pool_                    = pool;
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

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
      var pool = Interlocked.Exchange(ref pool_,
                                      null);
      if (pool is not null)
      {
        await pool.Release(Value,
                           exception_,
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
    ~Guard()
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
    ///   Acquire a new object from the pool, and create the guard managing it.
    /// </summary>
    /// <param name="pool">ObjectPool to acquire the object from</param>
    /// <param name="cancellationToken">Cancellation token used for stopping the Acquire and the Release of a new object</param>
    /// <exception cref="OperationCanceledException">Exception thrown when the cancellation is requested</exception>
    /// <returns>Newly created guard</returns>
    [PublicAPI]
    internal static async ValueTask<Guard> Create(ObjectPool<T>     pool,
                                                  CancellationToken cancellationToken)
    {
      var obj = await pool.Acquire(cancellationToken)
                          .ConfigureAwait(false);

      return new Guard(pool,
                       obj,
                       cancellationToken);
    }

    /// <summary>
    ///   Record an external exception within the guard
    /// </summary>
    /// <param name="exception">Exception that occured while the guarded object was being used</param>
    public void RecordException(Exception exception)
      => exception_ = exception;

    /// <summary>
    ///   Get the acquired object
    /// </summary>
    /// <param name="guard">Guard to get the object from</param>
    /// <returns>The acquired object</returns>
    [PublicAPI]
    public static implicit operator T(Guard guard)
      => guard.Value;
  }
}

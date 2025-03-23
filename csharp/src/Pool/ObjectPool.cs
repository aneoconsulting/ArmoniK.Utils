// This file is part of the ArmoniK project
//
// Copyright (C) ANEO, 2022-2025. All rights reserved.
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
///       // Acquire an object with a guard.
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
  private readonly Func<CancellationToken, ValueTask<(T, Func<Exception?, CancellationToken, ValueTask>)>> acquire_;
  private          RefCounted?                                                                             dispose_;

  private ObjectPool(Func<CancellationToken, ValueTask<(T, Func<Exception?, CancellationToken, ValueTask>)>> acquire,
                     RefCounted?                                                                             dispose)
  {
    acquire_ = acquire;
    dispose_ = dispose?.AcquireRef();
  }

  /// <summary>
  ///   Create a new ObjectPool with the given policy.
  /// </summary>
  /// <param name="policy">How the pool is configured</param>
  [PublicAPI]
  public ObjectPool(PoolPolicy<T> policy)
    : this(new PoolRaw<T>(policy))
  {
  }

  /// <summary>
  ///   Create a new ObjectPool from a raw pool.
  /// </summary>
  /// <param name="pool">Raw pool</param>
  [PublicAPI]
  public ObjectPool(IPoolRaw<T> pool)
    : this(async cancellationToken =>
           {
             var x = await pool.AcquireAsync(cancellationToken)
                               .ConfigureAwait(false);
             return (x, (e,
                         ct) => pool.ReleaseAsync(x,
                                                  e,
                                                  ct));
           },
           new RefCounted(pool))
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
  [PublicAPI]
  public ObjectPool(int                                          max,
                    Func<CancellationToken, ValueTask<T>>        createFunc,
                    Func<T, CancellationToken, ValueTask<bool>>? validateFunc = null)
    : this(new PoolPolicy<T>().SetMaxNumberOfInstances(max)
                              .SetCreate(createFunc)
                              .SetValidate(validateFunc))
  {
  }

  /// <summary>
  ///   Create a new ObjectPool without limit on the number of objects created.
  /// </summary>
  /// <param name="createFunc">Function to call to create new objects</param>
  /// <param name="validateFunc">Function to call to check if an object is still valid and can be safely reused</param>
  [PublicAPI]
  public ObjectPool(Func<CancellationToken, ValueTask<T>>        createFunc,
                    Func<T, CancellationToken, ValueTask<bool>>? validateFunc = null)
    : this(-1,
           createFunc,
           validateFunc)
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
  [PublicAPI]
  public ObjectPool(int            max,
                    Func<T>        createFunc,
                    Func<T, bool>? validateFunc = null)
    : this(new PoolPolicy<T>().SetMaxNumberOfInstances(max)
                              .SetCreate(createFunc)
                              .SetValidate(validateFunc))
  {
  }

  /// <summary>
  ///   Create a new ObjectPool without limit on the number of objects created.
  /// </summary>
  /// <param name="createFunc">Function to call to create new objects</param>
  /// <param name="validateFunc">Function to call to check if an object is still valid and can be safely reused</param>
  [PublicAPI]
  public ObjectPool(Func<T>        createFunc,
                    Func<T, bool>? validateFunc = null)
    : this(-1,
           createFunc,
           validateFunc)
  {
  }

  /// <inheritdoc />
  public ValueTask DisposeAsync()
    => Interlocked.Exchange(ref dispose_,
                            null)
                  ?.ReleaseRef()
                  ?.DisposeAsync() ?? new ValueTask();

  /// <inheritdoc />
  public void Dispose()
    => Interlocked.Exchange(ref dispose_,
                            null)
                  ?.ReleaseRef()
                  ?.Dispose();

  /// <summary>
  ///   Create a new ObjectPool where the objects are taken from the original pool,
  ///   and transformed by the projection to be stored in the PoolGuard
  /// </summary>
  /// <param name="projection">Function that transform the pooled objected into another</param>
  /// <typeparam name="TOut">Type of the objects in the new object pool</typeparam>
  /// <returns>ObjectPool</returns>
  [PublicAPI]
  public ObjectPool<TOut> Project<TOut>(Func<T, TOut> projection)
    => new(async ct =>
           {
             var (x, release) = await acquire_(ct)
                                  .ConfigureAwait(false);
             try
             {
               var y = projection(x);
               return (y, release);
             }
             catch (Exception e)
             {
               await release(e,
                             ct)
                 .ConfigureAwait(false);
               throw;
             }
           },
           dispose_);

  /// <summary>
  ///   Create a new ObjectPool where the objects are taken from the original pool,
  ///   and transformed by the projection to be stored in the PoolGuard
  /// </summary>
  /// <param name="projection">Function that transform the pooled objected into another</param>
  /// <typeparam name="TOut">Type of the objects in the new object pool</typeparam>
  /// <returns>ObjectPool</returns>
  [PublicAPI]
  public ObjectPool<TOut> Project<TOut>(Func<T, CancellationToken, ValueTask<TOut>> projection)
    => new(async ct =>
           {
             var (x, release) = await acquire_(ct)
                                  .ConfigureAwait(false);
             try
             {
               var y = await projection(x,
                                        ct)
                         .ConfigureAwait(false);
               return (y, release);
             }
             catch (Exception e)
             {
               await release(e,
                             ct)
                 .ConfigureAwait(false);
               throw;
             }
           },
           dispose_);

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
  public async ValueTask<PoolGuard<T>> GetAsync(CancellationToken cancellationToken = default)
  {
    var (x, release) = await acquire_(cancellationToken)
                         .ConfigureAwait(false);
    return new PoolGuard<T>(x,
                            release,
                            cancellationToken);
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
  /// <param name="projection">Function that transform the pooled objected into another</param>
  /// <param name="cancellationToken">Cancellation token used for stopping the Acquire of a new object</param>
  /// <exception cref="OperationCanceledException">Exception thrown when the cancellation is requested</exception>
  /// <returns>A guard that contains the acquired object.</returns>
  [PublicAPI]
  public async ValueTask<PoolGuard<TOut>> GetAsync<TOut>(Func<T, TOut>     projection,
                                                         CancellationToken cancellationToken = default)
  {
    var (x, release) = await acquire_(cancellationToken)
                         .ConfigureAwait(false);
    try
    {
      var y = projection(x);
      return new PoolGuard<TOut>(y,
                                 release,
                                 cancellationToken);
    }
    catch (Exception e)
    {
      await release(e,
                    cancellationToken)
        .ConfigureAwait(false);
      throw;
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
  /// <param name="projection">Function that transform the pooled objected into another</param>
  /// <param name="cancellationToken">Cancellation token used for stopping the Acquire of a new object</param>
  /// <exception cref="OperationCanceledException">Exception thrown when the cancellation is requested</exception>
  /// <returns>A guard that contains the acquired object.</returns>
  [PublicAPI]
  public async ValueTask<PoolGuard<TOut>> GetAsync<TOut>(Func<T, ValueTask<TOut>> projection,
                                                         CancellationToken        cancellationToken = default)
  {
    var (x, release) = await acquire_(cancellationToken)
                         .ConfigureAwait(false);
    try
    {
      var y = await projection(x)
                .ConfigureAwait(false);
      return new PoolGuard<TOut>(y,
                                 release,
                                 cancellationToken);
    }
    catch (Exception e)
    {
      await release(e,
                    cancellationToken)
        .ConfigureAwait(false);
      throw;
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
  /// <returns>A guard that contains the acquired object.</returns>
  [PublicAPI]
  public PoolGuard<T> Get()
    => GetAsync()
      .WaitSync();

  /// <summary>
  ///   Acquire a new object from the pool, creating it if there is no object in the pool.
  ///   If the limit of object has been reached, this method will wait for an object to be released.
  ///   The method returns a guard that contains the acquired object, and that automatically release the object when
  ///   disposed.
  /// </summary>
  /// <remarks>
  ///   If an exception is thrown during the creation of the object, acquire is cancelled (semaphore is released).
  /// </remarks>
  /// <param name="projection">Function that transform the pooled objected into another</param>
  /// <returns>A guard that contains the acquired object.</returns>
  [PublicAPI]
  public PoolGuard<TOut> Get<TOut>(Func<T, TOut> projection)
    => GetAsync(projection)
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
    await using var guard = await GetAsync(cancellationToken)
                              .ConfigureAwait(false);
    try
    {
      return f(guard.Value);
    }
    catch (Exception e)
    {
      guard.Exception = e;
      throw;
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
    await using var guard = await GetAsync(cancellationToken)
                              .ConfigureAwait(false);
    try
    {
      f(guard.Value);
    }
    catch (Exception e)
    {
      guard.Exception = e;
      throw;
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
    await using var guard = await GetAsync(cancellationToken)
                              .ConfigureAwait(false);
    try
    {
      return await f(guard.Value)
               .ConfigureAwait(false);
    }
    catch (Exception e)
    {
      guard.Exception = e;
      throw;
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
    await using var guard = await GetAsync(cancellationToken)
                              .ConfigureAwait(false);
    try
    {
      await f(guard.Value)
        .ConfigureAwait(false);
    }
    catch (Exception e)
    {
      guard.Exception = e;
      throw;
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
    using var guard = Get();
    try
    {
      return f(guard.Value);
    }
    catch (Exception e)
    {
      guard.Exception = e;
      throw;
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
    using var guard = Get();
    try
    {
      f(guard.Value);
    }
    catch (Exception e)
    {
      guard.Exception = e;
      throw;
    }
  }
}

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
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

using JetBrains.Annotations;

namespace ArmoniK.Utils;

public class ObjectPool<T> : IDisposable, IAsyncDisposable
{
  private readonly ConcurrentBag<T>                             bag_;
  private readonly Func<CancellationToken, ValueTask<T>>        createFunc_;
  private readonly Func<T, CancellationToken, ValueTask<bool>>? returnFunc_;
  private readonly SemaphoreSlim                                sem_;

  [PublicAPI]
  public ObjectPool(int?                                         max,
                    Func<CancellationToken, ValueTask<T>>        createFunc,
                    Func<T, CancellationToken, ValueTask<bool>>? returnFunc = null)
  {
    bag_        = new ConcurrentBag<T>();
    sem_        = new SemaphoreSlim(max ?? int.MaxValue);
    createFunc_ = createFunc;
    returnFunc_ = returnFunc;
  }

  public async ValueTask DisposeAsync()
  {
    while (bag_.TryTake(out var obj))
    {
      await DisposeOneAsync(obj)
        .ConfigureAwait(false);
    }

    sem_.Dispose();
    GC.SuppressFinalize(this);
  }

  public void Dispose()
  {
    while (bag_.TryTake(out var obj))
    {
      DisposeOne(obj);
    }

    sem_.Dispose();
    GC.SuppressFinalize(this);
  }

  ~ObjectPool()
    => Dispose();

  private async ValueTask<T> Acquire(CancellationToken cancellationToken = default)
  {
    var bag = bag_;
    if (bag is null)
    {
      throw new ObjectDisposedException("ObjectPool has been disposed");
    }

    await sem_.WaitAsync(cancellationToken)
              .ConfigureAwait(false);

    if (bag.TryTake(out var res))
    {
      return res;
    }

    return await createFunc_(cancellationToken)
             .ConfigureAwait(false);
  }

  private async ValueTask Release(T                 obj,
                                  CancellationToken cancellationToken = default)
  {
    var bag = bag_;
    if (bag is null)
    {
      throw new ObjectDisposedException("ObjectPool has been disposed");
    }

    var isValid = returnFunc_ is null || await returnFunc_(obj,
                                                           cancellationToken)
                    .ConfigureAwait(false);

    if (isValid)
    {
      bag.Add(obj);
    }

    sem_.Release();
  }

  private static void DisposeOne(T obj)
  {
    switch (obj)
    {
      case IDisposable disposable:
        disposable.Dispose();
        break;
      case IAsyncDisposable asyncDisposable:
        asyncDisposable.DisposeAsync()
                       .GetAwaiter()
                       .GetResult();
        break;
    }
  }

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

  [PublicAPI]
  public ValueTask<Guard> GetAsync(CancellationToken cancellationToken = default)
    => Guard.Create(this,
                    cancellationToken);

  [PublicAPI]
  public async ValueTask<TOut> CallWithAsync<TOut>(Func<T, TOut>     f,
                                                   CancellationToken cancellationToken = default)
  {
    await using var guard = await GetAsync(cancellationToken)
                              .ConfigureAwait(false);

    return f(guard.Value);
  }

  [PublicAPI]
  public async ValueTask CallWithAsync(Action<T>         f,
                                       CancellationToken cancellationToken = default)
  {
    await using var guard = await GetAsync(cancellationToken)
                              .ConfigureAwait(false);

    f(guard.Value);
  }


  [PublicAPI]
  public async ValueTask<TOut> CallWithAsync<TOut>(Func<T, ValueTask<TOut>> f,
                                                   CancellationToken        cancellationToken = default)
  {
    await using var guard = await GetAsync(cancellationToken)
                              .ConfigureAwait(false);

    return await f(guard.Value)
             .ConfigureAwait(false);
  }

  [PublicAPI]
  public async ValueTask CallWithAsync(Func<T, ValueTask> f,
                                       CancellationToken  cancellationToken = default)
  {
    await using var guard = await GetAsync(cancellationToken)
                              .ConfigureAwait(false);

    await f(guard.Value)
      .ConfigureAwait(false);
  }

  public sealed class Guard : IDisposable, IAsyncDisposable
  {
    private ObjectPool<T>? pool_;

    private Guard(ObjectPool<T> pool,
                  T             obj)
    {
      pool_ = pool;
      Value = obj;
    }

    public T Value { get; }

    public async ValueTask DisposeAsync()
    {
      var pool = Interlocked.Exchange(ref pool_,
                                      null);
      if (pool is not null)
      {
        await pool.Release(Value)
                  .ConfigureAwait(false);
        GC.SuppressFinalize(this);
      }
    }

    public void Dispose()
      => DisposeAsync()
         .GetAwaiter()
         .GetResult();

    ~Guard()
      => Dispose();

    [PublicAPI]
    internal static async ValueTask<Guard> Create(ObjectPool<T>     pool,
                                                  CancellationToken cancellationToken)
    {
      var obj = await pool.Acquire(cancellationToken)
                          .ConfigureAwait(false);

      return new Guard(pool,
                       obj);
    }

    [PublicAPI]
    public static implicit operator T(Guard guard)
      => guard.Value;
  }
}

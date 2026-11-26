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
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using JetBrains.Annotations;

namespace ArmoniK.Utils;

/// <summary>
///   Async enumerable and enumerator over a single enumeration of a source, that can peek at upcoming elements without
///   consuming them.
/// </summary>
/// <remarks>
///   <para>
///     Like LINQ async iterators, an <see cref="AsyncPeekable{T}" /> is its own enumerator:
///     <see cref="GetAsyncEnumerator" /> returns the instance itself. Elements are consumed by
///     <see cref="MoveNextAsync" />, either directly or through an <c>await foreach</c>.
///   </para>
///   <para>
///     The source is enumerated at most once, lazily, on the first peek or <see cref="MoveNextAsync" />.
///     Peeked elements are buffered until they are consumed.
///     Peeking is relative to the current position: before the first <see cref="MoveNextAsync" />,
///     <see cref="PeekAsync()" /> returns the first element.
///   </para>
///   <para>
///     Disposing releases the source and the buffered elements, after which the instance behaves as exhausted.
///     As <c>await foreach</c> disposes its enumerator, this happens at the end of an <c>await foreach</c>, including
///     when exiting it early (e.g. with <c>break</c>).
///   </para>
///   <para>
///     This class is not thread-safe: there must be at most one pending operation at a time
///     (peek, <see cref="MoveNextAsync" /> or <see cref="DisposeAsync" />).
///   </para>
/// </remarks>
/// <typeparam name="T">Type of the elements</typeparam>
[PublicAPI]
public sealed class AsyncPeekable<T> : IAsyncEnumerable<T>, IAsyncEnumerator<T>
{
  private readonly Queue<T>             buffer_ = new();
  private readonly CancellationToken    sourceCancellationToken_;
  private          bool                 completed_;
  private          CancellationToken    enumerationCancellationToken_;
  private          IAsyncEnumerator<T>? enumerator_;
  private          IAsyncEnumerable<T>? source_;

  /// <summary>
  ///   Create a peekable over <paramref name="source" />.
  /// </summary>
  /// <param name="source">Elements to enumerate, <c>null</c> is treated as empty</param>
  /// <param name="cancellationToken">
  ///   Token given to the enumerator of <paramref name="source" />. If it cannot be cancelled, the token given to
  ///   <see cref="GetAsyncEnumerator" /> is used instead, provided the source has not been started yet.
  /// </param>
  public AsyncPeekable(IAsyncEnumerable<T>? source,
                       CancellationToken    cancellationToken = default)
  {
    source_                  = source;
    sourceCancellationToken_ = cancellationToken;
    completed_               = source is null;
  }

  /// <summary>
  ///   Create a peekable over the remaining elements of <paramref name="enumerator" />.
  /// </summary>
  /// <param name="enumerator">Enumerator to wrap, it is disposed when the peekable is disposed</param>
  public AsyncPeekable(IAsyncEnumerator<T> enumerator)
    => enumerator_ = enumerator ?? throw new ArgumentNullException(nameof(enumerator));

  /// <inheritdoc cref="GetAsyncEnumerator" />
  IAsyncEnumerator<T> IAsyncEnumerable<T>.GetAsyncEnumerator(CancellationToken cancellationToken)
    => GetAsyncEnumerator(cancellationToken);

  /// <inheritdoc />
  public T Current { get; private set; } = default!;

  /// <inheritdoc />
  public async ValueTask<bool> MoveNextAsync()
  {
    enumerationCancellationToken_.ThrowIfCancellationRequested();

    // Fast path: nothing has been peeked, read directly from the source without buffering
    if (buffer_.Count == 0)
    {
      var (success, value) = await TryMoveNextAsync()
                               .ConfigureAwait(false);
      Current = value;
      return success;
    }

    Current = buffer_.Dequeue();
    return true;
  }

  /// <summary>
  ///   Release the source and the buffered elements. The instance then behaves as exhausted.
  /// </summary>
  public async ValueTask DisposeAsync()
  {
    completed_ = true;
    Current    = default!;
    source_    = null;
    buffer_.Clear();

    var enumerator = enumerator_;
    enumerator_ = null;
    if (enumerator is not null)
    {
      await enumerator.DisposeAsync()
                      .ConfigureAwait(false);
    }
  }

  /// <summary>
  ///   Get the enumerator, which is this instance.
  /// </summary>
  /// <remarks>
  ///   The enumeration resumes at the current position.
  ///   Disposing the enumerator (as <c>await foreach</c> does) disposes this instance.
  /// </remarks>
  /// <param name="cancellationToken">
  ///   Token checked before each <see cref="MoveNextAsync" />. It is also given to the source if it has not been
  ///   started yet and no cancellable token was given at construction.
  /// </param>
  /// <returns>This instance</returns>
  public AsyncPeekable<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
  {
    enumerationCancellationToken_ = cancellationToken;
    return this;
  }

  /// <summary>
  ///   Get the next element without consuming it.
  /// </summary>
  /// <returns>The next element</returns>
  /// <exception cref="InvalidOperationException">There are no more elements</exception>
  public async ValueTask<T> PeekAsync()
  {
    var (success, value) = await TryPeekAsync()
                             .ConfigureAwait(false);
    return success
             ? value
             : throw new InvalidOperationException("There are no more elements to peek");
  }

  /// <summary>
  ///   Get up to <paramref name="count" /> next elements without consuming them.
  /// </summary>
  /// <param name="count">Maximum number of elements to peek</param>
  /// <returns>
  ///   The next elements. If there are less than <paramref name="count" /> remaining elements, all of them are returned.
  /// </returns>
  /// <exception cref="ArgumentOutOfRangeException"><paramref name="count" /> is negative</exception>
  public ValueTask<T[]> PeekAsync(int count)
  {
    // Validated eagerly, so the exception is thrown by the call itself
    if (count < 0)
    {
      throw new ArgumentOutOfRangeException(nameof(count),
                                            count,
                                            "Cannot peek a negative number of elements");
    }

    return Core();

    async ValueTask<T[]> Core()
    {
      var n = await FillAsync(count)
                .ConfigureAwait(false);
      return buffer_.Take(n)
                    .ToArray();
    }
  }

  /// <summary>
  ///   Try to get the next element without consuming it.
  /// </summary>
  /// <returns>
  ///   Whether there is a next element, and the next element if there is one (<c>default</c> otherwise)
  /// </returns>
  public async ValueTask<(bool Success, T Value)> TryPeekAsync()
  {
    if (await FillAsync(1)
          .ConfigureAwait(false) == 0)
    {
      return (false, default!);
    }

    return (true, buffer_.Peek());
  }

  // Buffer elements until there are at least count of them or the source is exhausted.
  // Returns the number of buffered elements, capped to count.
  private async ValueTask<int> FillAsync(int count)
  {
    while (buffer_.Count < count)
    {
      var (success, value) = await TryMoveNextAsync()
                               .ConfigureAwait(false);
      if (!success)
      {
        break;
      }

      buffer_.Enqueue(value);
    }

    return Math.Min(buffer_.Count,
                    count);
  }

  private async ValueTask<(bool Success, T Value)> TryMoveNextAsync()
  {
    if (completed_)
    {
      return (false, default!);
    }

    if (enumerator_ is null)
    {
      enumerator_ = source_!.GetAsyncEnumerator(sourceCancellationToken_.CanBeCanceled
                                                  ? sourceCancellationToken_
                                                  : enumerationCancellationToken_);
      source_ = null;
    }

    if (await enumerator_.MoveNextAsync()
                         .ConfigureAwait(false))
    {
      return (true, enumerator_.Current);
    }

    // Release the source as soon as it is exhausted
    completed_ = true;
    var enumerator = enumerator_;
    enumerator_ = null;
    await enumerator.DisposeAsync()
                    .ConfigureAwait(false);

    return (false, default!);
  }
}

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
using System.Collections;
using System.Collections.Generic;

using JetBrains.Annotations;

namespace ArmoniK.Utils;

/// <summary>
///   Enumerable and enumerator over a single enumeration of a source, that can peek at upcoming elements without
///   consuming them.
/// </summary>
/// <remarks>
///   <para>
///     Like LINQ iterators, a <see cref="Peekable{T}" /> is its own enumerator: <see cref="GetEnumerator" /> returns
///     the instance itself. Elements are consumed by <see cref="MoveNext" />, either directly or through a
///     <c>foreach</c>.
///   </para>
///   <para>
///     The source is enumerated at most once, lazily, on the first peek or <see cref="MoveNext" />.
///     Peeked elements are buffered until they are consumed.
///     Peeking is relative to the current position: before the first <see cref="MoveNext" />, <see cref="Peek()" />
///     returns the first element.
///   </para>
///   <para>
///     Disposing releases the source and the buffered elements, after which the instance behaves as exhausted.
///     As <c>foreach</c> disposes its enumerator, this happens at the end of a <c>foreach</c>, including when exiting
///     it early (e.g. with <c>break</c>).
///   </para>
///   <para>
///     This class is not thread-safe.
///   </para>
/// </remarks>
/// <typeparam name="T">Type of the elements</typeparam>
[PublicAPI]
public sealed class Peekable<T> : IEnumerable<T>, IEnumerator<T>
{
  private readonly List<T>         buffer_ = new();
  private          bool            completed_;
  private          T               current_ = default!;
  private          IEnumerator<T>? enumerator_;
  private          int             head_;
  private          IEnumerable<T>? source_;

  /// <summary>
  ///   Create a peekable over <paramref name="source" />.
  /// </summary>
  /// <param name="source">Elements to enumerate, <c>null</c> is treated as empty</param>
  public Peekable(IEnumerable<T>? source)
  {
    source_    = source;
    completed_ = source is null;
  }

  /// <summary>
  ///   Create a peekable over the remaining elements of <paramref name="enumerator" />.
  /// </summary>
  /// <param name="enumerator">Enumerator to wrap, it is disposed when the peekable is disposed</param>
  public Peekable(IEnumerator<T> enumerator)
    => enumerator_ = enumerator ?? throw new ArgumentNullException(nameof(enumerator));

  private int Count
    => buffer_.Count - head_;

  /// <inheritdoc cref="GetEnumerator" />
  IEnumerator<T> IEnumerable<T>.GetEnumerator()
    => this;

  /// <inheritdoc cref="GetEnumerator" />
  IEnumerator IEnumerable.GetEnumerator()
    => this;

  /// <inheritdoc />
  public T Current
    => current_;

  /// <inheritdoc />
  object? IEnumerator.Current
    => current_;

  /// <inheritdoc />
  public bool MoveNext()
  {
    // Fast path: nothing has been peeked, read directly from the source without buffering
    if (Count == 0)
    {
      return TryMoveNext(out current_);
    }

    current_       = buffer_[head_];
    buffer_[head_] = default!; // Release the reference
    head_++;

    if (head_ == buffer_.Count)
    {
      buffer_.Clear();
      head_ = 0;
    }
    else if (head_ >= 32 && 2 * head_ >= buffer_.Count)
    {
      buffer_.RemoveRange(0,
                          head_);
      head_ = 0;
    }

    return true;
  }

  /// <summary>
  ///   Not supported.
  /// </summary>
  /// <exception cref="NotSupportedException">Always thrown</exception>
  public void Reset()
    => throw new NotSupportedException();

  /// <summary>
  ///   Release the source and the buffered elements. The instance then behaves as exhausted.
  /// </summary>
  public void Dispose()
  {
    completed_ = true;
    current_   = default!;
    source_    = null;
    buffer_.Clear();
    head_ = 0;

    var enumerator = enumerator_;
    enumerator_ = null;
    enumerator?.Dispose();
  }

  /// <summary>
  ///   Get the enumerator, which is this instance.
  /// </summary>
  /// <remarks>
  ///   The enumeration resumes at the current position.
  ///   Disposing the enumerator (as <c>foreach</c> does) disposes this instance.
  /// </remarks>
  /// <returns>This instance</returns>
  public Peekable<T> GetEnumerator()
    => this;

  /// <summary>
  ///   Get the next element without consuming it.
  /// </summary>
  /// <returns>The next element</returns>
  /// <exception cref="InvalidOperationException">There are no more elements</exception>
  public T Peek()
    => TryPeek(out var value)
         ? value
         : throw new InvalidOperationException("There are no more elements to peek");

  /// <summary>
  ///   Get up to <paramref name="count" /> next elements without consuming them.
  /// </summary>
  /// <param name="count">Maximum number of elements to peek</param>
  /// <returns>
  ///   The next elements. If there are less than <paramref name="count" /> remaining elements, all of them are returned.
  /// </returns>
  /// <exception cref="ArgumentOutOfRangeException"><paramref name="count" /> is negative</exception>
  public T[] Peek(int count)
  {
    if (count < 0)
    {
      throw new ArgumentOutOfRangeException(nameof(count),
                                            count,
                                            "Cannot peek a negative number of elements");
    }

    var n = Fill(count);
    var result = n == 0
                   ? Array.Empty<T>()
                   : new T[n];
    buffer_.CopyTo(head_,
                   result,
                   0,
                   n);
    return result;
  }

  /// <summary>
  ///   Try to get the next element without consuming it.
  /// </summary>
  /// <param name="value">The next element if there is one, <c>default</c> otherwise</param>
  /// <returns>Whether there is a next element</returns>
  public bool TryPeek(out T value)
  {
    if (Fill(1) == 0)
    {
      value = default!;
      return false;
    }

    value = buffer_[head_];
    return true;
  }

  // Buffer elements until there are at least count of them or the source is exhausted.
  // Returns the number of buffered elements, capped to count.
  private int Fill(int count)
  {
    while (Count < count && TryMoveNext(out var value))
    {
      buffer_.Add(value);
    }

    return Math.Min(Count,
                    count);
  }

  private bool TryMoveNext(out T value)
  {
    if (completed_)
    {
      value = default!;
      return false;
    }

    if (enumerator_ is null)
    {
      enumerator_ = source_!.GetEnumerator();
      source_     = null;
    }

    if (enumerator_.MoveNext())
    {
      value = enumerator_.Current;
      return true;
    }

    // Release the source as soon as it is exhausted
    completed_ = true;
    var enumerator = enumerator_;
    enumerator_ = null;
    enumerator.Dispose();

    value = default!;
    return false;
  }
}

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
using System.Threading;
using System.Threading.Tasks;

namespace ArmoniK.Utils;

/// <summary>
///   Async enumerable that rechunks a sequence of memory chunks so that every chunk,
///   except the last one, has a size between a minimum and a maximum.
/// </summary>
/// <remarks>
///   <para>
///     Input chunks whose size is already within bounds are yielded as is, and oversized input chunks are sliced,
///     without any copy. Only fragments smaller than the minimum size are copied into a buffer, in order to be
///     merged with the following input chunks.
///   </para>
///   <para>
///     No reference to an input chunk is kept across a call to the source <c>MoveNextAsync</c>:
///     the source is free to reuse its memory once its next element has been requested.
///   </para>
/// </remarks>
/// <typeparam name="T">Type of the elements</typeparam>
internal sealed class Rechunker<T> : IAsyncEnumerable<ReadOnlyMemory<T>>
{
  private readonly int                                 maxSize_;
  private readonly int                                 minSize_;
  private readonly IAsyncEnumerable<ReadOnlyMemory<T>> source_;

  /// <summary>
  ///   Create a rechunker over <paramref name="source" />.
  /// </summary>
  /// <param name="source">Input chunks</param>
  /// <param name="minSize">Minimum size of the output chunks (except the last one), must be at least 1</param>
  /// <param name="maxSize">Maximum size of the output chunks, must be at least <paramref name="minSize" /></param>
  public Rechunker(IAsyncEnumerable<ReadOnlyMemory<T>> source,
                   int                                 minSize,
                   int                                 maxSize)
  {
    source_  = source;
    minSize_ = minSize;
    maxSize_ = maxSize;
  }

  /// <inheritdoc />
  public IAsyncEnumerator<ReadOnlyMemory<T>> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    => new Enumerator(source_.GetAsyncEnumerator(cancellationToken),
                      minSize_,
                      maxSize_,
                      cancellationToken);

  private sealed class Enumerator : IAsyncEnumerator<ReadOnlyMemory<T>>
  {
    private readonly CancellationToken                   cancellationToken_;
    private readonly int                                 maxSize_;
    private readonly int                                 minSize_;
    private readonly IAsyncEnumerator<ReadOnlyMemory<T>> source_;

    // Accumulation buffer of size minSize_, allocated lazily.
    // Only the first count_ elements are valid, and count_ is always below minSize_ between two calls.
    private T[]? buffer_;
    private int  count_;

    // Part of the current input chunk that has not been processed yet
    private ReadOnlyMemory<T> pending_;
    private bool              sourceCompleted_;

    public Enumerator(IAsyncEnumerator<ReadOnlyMemory<T>> source,
                      int                                 minSize,
                      int                                 maxSize,
                      CancellationToken                   cancellationToken)
    {
      source_            = source;
      minSize_           = minSize;
      maxSize_           = maxSize;
      cancellationToken_ = cancellationToken;
    }

    public ReadOnlyMemory<T> Current { get; private set; }

    public async ValueTask<bool> MoveNextAsync()
    {
      cancellationToken_.ThrowIfCancellationRequested();

      while (true)
      {
        if (TryProduce(out var chunk))
        {
          Current = chunk;
          return true;
        }

        if (sourceCompleted_)
        {
          // Last chunk can be smaller than minSize_
          if (count_ > 0)
          {
            Current = new ReadOnlyMemory<T>(buffer_,
                                            0,
                                            count_);
            buffer_ = null;
            count_  = 0;
            return true;
          }

          Current = default;
          return false;
        }

        // pending_ is empty here: it is safe to let the source reuse its memory
        if (await source_.MoveNextAsync()
                         .ConfigureAwait(false))
        {
          pending_ = source_.Current;
        }
        else
        {
          sourceCompleted_ = true;
        }
      }
    }

    public ValueTask DisposeAsync()
    {
      pending_ = default;
      buffer_  = null;
      count_   = 0;
      Current  = default;
      return source_.DisposeAsync();
    }

    /// <summary>
    ///   Consume <see cref="pending_" /> to produce an output chunk.
    ///   If no chunk can be produced, <see cref="pending_" /> is entirely consumed.
    /// </summary>
    /// <param name="chunk">Produced chunk</param>
    /// <returns>Whether a chunk has been produced</returns>
    private bool TryProduce(out ReadOnlyMemory<T> chunk)
    {
      chunk = default;

      if (pending_.IsEmpty)
      {
        return false;
      }

      var length = pending_.Length;

      if (count_ == 0)
      {
        // Fragment too small: it must be merged with the next input chunks
        if (length < minSize_)
        {
          Append(length);
          return false;
        }

        // Large enough: slice it without copy
        int size;
        if (length <= maxSize_)
        {
          size = length;
        }
        else if (length - maxSize_ >= minSize_)
        {
          size = maxSize_;
        }
        else if (length - minSize_ >= minSize_)
        {
          // Leave exactly minSize_ elements so that the remainder can also be yielded without copy
          size = length - minSize_;
        }
        else
        {
          // Cannot be split in two valid chunks: minimize the remainder that will be copied
          size = maxSize_;
        }

        chunk = pending_.Slice(0,
                               size);
        pending_ = pending_.Slice(size);
        return true;
      }

      // Buffer is not empty: complete it
      var missing = minSize_ - count_;
      if (length < missing)
      {
        Append(length);
        return false;
      }

      // If what remains after reaching minSize_ cannot be yielded on its own, it would have to be copied anyway:
      // absorb as much as possible into this chunk instead
      var take = length - missing >= minSize_
                   ? missing
                   : Math.Min(length,
                              maxSize_ - count_);

      if (take == missing)
      {
        // Buffer becomes exactly full: yield it as is, and allocate a fresh one for the next chunk
        Append(take);
        chunk   = buffer_!;
        buffer_ = null;
        count_  = 0;
      }
      else
      {
        // Chunk is larger than the buffer: allocate it with its exact size, and keep the buffer for later
        var array = new T[count_ + take];
        buffer_!.AsSpan(0,
                        count_)
                .CopyTo(array);
        pending_.Span.Slice(0,
                            take)
                .CopyTo(array.AsSpan(count_));
        pending_ = pending_.Slice(take);
        chunk    = array;
        count_   = 0;
      }

      return true;
    }

    /// <summary>
    ///   Copy the first <paramref name="n" /> elements of <see cref="pending_" /> into the buffer.
    /// </summary>
    private void Append(int n)
    {
      buffer_ ??= new T[minSize_];
      pending_.Span.Slice(0,
                          n)
              .CopyTo(buffer_.AsSpan(count_));
      pending_ =  pending_.Slice(n);
      count_   += n;
    }
  }
}

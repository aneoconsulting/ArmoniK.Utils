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
using System.Threading.Channels;
using System.Threading.Tasks;

using NUnit.Framework;

namespace ArmoniK.Utils.Tests;

public class ChannelExtTest
{
  [Test]
  [AbortAfter(1000)]
  public async Task ToAsyncEnumerableReadsAll([Values(0,
                                                      1,
                                                      10)]
                                              int n)
  {
    var channel = Channel.CreateUnbounded<int>();

    for (var i = 0; i < n; ++i)
    {
      channel.Writer.TryWrite(i);
    }

    channel.Writer.Complete();

    var list = new List<int>();
    await foreach (var x in channel.Reader.ToAsyncEnumerable(CancellationToken.None))
    {
      list.Add(x);
    }

    Assert.That(list,
                Is.EqualTo(GenerateInts(n)));
  }

  [Test]
  [AbortAfter(1000)]
  public async Task ToAsyncEnumerableWaitsForItems()
  {
    var channel = Channel.CreateUnbounded<int>();

    var producer = Task.Run(async () =>
                            {
                              for (var i = 0; i < 10; ++i)
                              {
                                await Task.Delay(10)
                                          .ConfigureAwait(false);
                                channel.Writer.TryWrite(i);
                              }

                              channel.Writer.Complete();
                            });

    var list = new List<int>();
    await foreach (var x in channel.Reader.ToAsyncEnumerable(CancellationToken.None))
    {
      list.Add(x);
    }

    await producer.ConfigureAwait(false);

    Assert.That(list,
                Is.EqualTo(GenerateInts(10)));
  }

  [Test]
  [AbortAfter(1000)]
  public async Task ToAsyncEnumerableThrowsCompletionError()
  {
    var channel = Channel.CreateUnbounded<int>();

    channel.Writer.TryWrite(0);
    channel.Writer.TryWrite(1);
    channel.Writer.Complete(new ApplicationException());

    await using var enumerator = channel.Reader.ToAsyncEnumerable(CancellationToken.None)
                                        .GetAsyncEnumerator();

    // Items written before the error are still read
    for (var i = 0; i < 2; ++i)
    {
      Assert.That(await enumerator.MoveNextAsync()
                                  .ConfigureAwait(false),
                  Is.True);
      Assert.That(enumerator.Current,
                  Is.EqualTo(i));
    }

    Assert.That(enumerator.MoveNextAsync,
                Throws.TypeOf<ApplicationException>());
  }

  [Test]
  [AbortAfter(1000)]
  public async Task ToAsyncEnumerableCancellation([Values] bool empty)
  {
    var channel = Channel.CreateUnbounded<int>();
    var cts     = new CancellationTokenSource();

    if (!empty)
    {
      channel.Writer.TryWrite(0);
      channel.Writer.TryWrite(1);
    }

    await using var enumerator = channel.Reader.ToAsyncEnumerable(cts.Token)
                                        .GetAsyncEnumerator();

    if (!empty)
    {
      Assert.That(await enumerator.MoveNextAsync()
                                  .ConfigureAwait(false),
                  Is.True);
    }

    cts.Cancel();

    // Cancellation is effective even if items are available
    Assert.That(enumerator.MoveNextAsync,
                Throws.InstanceOf<OperationCanceledException>());
  }

  private static IEnumerable<int> GenerateInts(int n)
  {
    for (var i = 0; i < n; ++i)
    {
      yield return i;
    }
  }
}

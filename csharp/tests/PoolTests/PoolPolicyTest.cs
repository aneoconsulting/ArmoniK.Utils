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

using ArmoniK.Utils.Pool;

using NUnit.Framework;
using NUnit.Framework.Constraints;

namespace ArmoniK.Utils.Tests.PoolTests;

[TestFixture(TestOf = typeof(PoolPolicy<>))]
public class PoolPolicyTest
{
  public enum CreateType
  {
    Sync,
    Async,
    AsyncCancellation,
  }

  public enum ThrowType
  {
    DoNotThrow,
    ThrowSync,
    ThrowAsync,
  }

  public enum ValidateSetter
  {
    Acquire,
    Release,
    Both,
  }

  public enum ValidateType
  {
    Sync,
    SyncException,
    Async,
    AsyncCancellation,
    AsyncException,
    AsyncExceptionCancellation,
  }

  [Test]
  [AbortAfter(1000)]
  public void Default()
  {
    var policy = new PoolPolicy<int>();

    Assert.That(() => policy.CreateAsync(),
                Throws.InstanceOf<NotSupportedException>());
    Assert.That(() => policy.CreateAsync(new CancellationToken(false)),
                Throws.InstanceOf<NotSupportedException>());
    Assert.That(() => policy.CreateAsync(new CancellationToken(true)),
                Throws.InstanceOf<NotSupportedException>());
    Assert.That(() => policy.ValidateAcquireAsync(0),
                Is.True);
    Assert.That(() => policy.ValidateAcquireAsync(0,
                                                  new CancellationToken(false)),
                Is.True);
    Assert.That(() => policy.ValidateAcquireAsync(0,
                                                  new CancellationToken(true)),
                Is.True);
    Assert.That(() => policy.ValidateReleaseAsync(0,
                                                  null),
                Is.True);
    Assert.That(() => policy.ValidateReleaseAsync(0,
                                                  new ApplicationException()),
                Is.True);
    Assert.That(() => policy.ValidateReleaseAsync(0,
                                                  new ApplicationException(),
                                                  new CancellationToken(false)),
                Is.True);
    Assert.That(() => policy.ValidateReleaseAsync(0,
                                                  new ApplicationException(),
                                                  new CancellationToken(true)),
                Is.True);
  }

  [Test]
  [AbortAfter(1000)]
  public void Create([Values] CreateType createType,
                     [Values] ThrowType  throwType,
                     [Values(null,
                             0,
                             1)]
                     int? delay)
  {
    var i          = 0;
    var policyOrig = new PoolPolicy<int>();
    var policy = createType switch
                 {
                   CreateType.Sync => policyOrig.SetCreate(() => Callback()
                                                             .WaitSync()),
                   CreateType.Async             => policyOrig.SetCreate(() => Callback()),
                   CreateType.AsyncCancellation => policyOrig.SetCreate(Callback),
                   _                            => throw new ArgumentOutOfRangeException(),
                 };

    Assert.That(policy,
                Is.SameAs(policyOrig));

    Func<IResolveConstraint?> throwConstraint = throwType is not ThrowType.DoNotThrow
                                                  ? Throws.InstanceOf<ApplicationException>
                                                  : () => null;

    AssertThatAsync(policy.CreateAsync(),
                    throwConstraint() ?? Is.EqualTo(1));
    Assert.That(i,
                Is.EqualTo(1));

    AssertThatAsync(policy.CreateAsync(new CancellationToken(false)),
                    throwConstraint() ?? Is.EqualTo(2));
    Assert.That(i,
                Is.EqualTo(2));

    var cancellationToken = new CancellationToken(true);
    Func<IResolveConstraint?> cancellationConstraint = createType is CreateType.AsyncCancellation
                                                         ? () => Throws.InstanceOf<OperationCanceledException>()
                                                                       .And.Property(nameof(OperationCanceledException.CancellationToken))
                                                                       .EqualTo(cancellationToken)
                                                         : () => null;

    AssertThatAsync(policy.CreateAsync(cancellationToken),
                    throwConstraint() ?? cancellationConstraint() ?? Is.EqualTo(3));
    Assert.That(i,
                Is.EqualTo(3));
    return;

    ValueTask<int> Callback(CancellationToken ct = default)
      => ValueTaskFactory(++i,
                          delay,
                          throwType,
                          ct);
  }

  [Test]
  [AbortAfter(1000)]
  public void Validate([Values] ValidateType validateType,
                       [Values] ThrowType    throwType,
                       [Values(null,
                               0,
                               1)]
                       int? delay)
  {
    var policyOrig = new PoolPolicy<bool>();
    var policy = validateType switch
                 {
                   ValidateType.Sync => policyOrig.SetValidate(x => Callback(x)
                                                                 .WaitSync()),
                   ValidateType.SyncException => policyOrig.SetValidate((x,
                                                                         e) => Callback(x,
                                                                                        e)
                                                                          .WaitSync()),
                   ValidateType.Async => policyOrig.SetValidate(x => Callback(x)),
                   ValidateType.AsyncCancellation => policyOrig.SetValidate((x,
                                                                             ct) => Callback(x,
                                                                                             ct: ct)),
                   ValidateType.AsyncException => policyOrig.SetValidate((x,
                                                                          e) => Callback(x,
                                                                                         e)),
                   ValidateType.AsyncExceptionCancellation => policyOrig.SetValidate(Callback),
                   _                                       => throw new ArgumentOutOfRangeException(),
                 };

    Assert.That(policy,
                Is.SameAs(policyOrig));

    ValidateAcquireImpl(policy,
                        validateType,
                        throwType);
    ValidateReleaseImpl(policy,
                        validateType,
                        throwType);
    return;

    ValueTask<bool> Callback(bool              x,
                             Exception?        e  = null,
                             CancellationToken ct = default)
      => ValueTaskFactory(e is null
                            ? !x
                            : x,
                          delay,
                          throwType,
                          ct);
  }

  [Test]
  [AbortAfter(1000)]
  public void ValidateAcquire([Values] ValidateType validateType,
                              [Values] ThrowType    throwType,
                              [Values(null,
                                      0,
                                      1)]
                              int? delay)
  {
    var policyOrig = new PoolPolicy<bool>();
    var policy = validateType switch
                 {
                   ValidateType.Sync => policyOrig.SetValidate(x => Callback(x)
                                                                 .WaitSync()),
                   ValidateType.SyncException => policyOrig.SetValidate((x,
                                                                         e) => Callback(x,
                                                                                        e)
                                                                          .WaitSync()),
                   ValidateType.Async => policyOrig.SetValidate(x => Callback(x)),
                   ValidateType.AsyncCancellation => policyOrig.SetValidate((x,
                                                                             ct) => Callback(x,
                                                                                             ct: ct)),
                   ValidateType.AsyncException => policyOrig.SetValidate((x,
                                                                          e) => Callback(x,
                                                                                         e)),
                   ValidateType.AsyncExceptionCancellation => policyOrig.SetValidate(Callback),
                   _                                       => throw new ArgumentOutOfRangeException(),
                 };

    Assert.That(policy,
                Is.SameAs(policyOrig));

    ValidateAcquireImpl(policy,
                        validateType,
                        throwType);

    return;

    ValueTask<bool> Callback(bool              x,
                             Exception?        e  = null,
                             CancellationToken ct = default)
      => ValueTaskFactory(e is null
                            ? !x
                            : x,
                          delay,
                          throwType,
                          ct);
  }

  [Test]
  [AbortAfter(1000)]
  public void ValidateRelease([Values] ValidateType validateType,
                              [Values] ThrowType    throwType,
                              [Values(null,
                                      0,
                                      1)]
                              int? delay)
  {
    var policyOrig = new PoolPolicy<bool>();
    var policy = validateType switch
                 {
                   ValidateType.Sync => policyOrig.SetValidate(x => Callback(x)
                                                                 .WaitSync()),
                   ValidateType.SyncException => policyOrig.SetValidate((x,
                                                                         e) => Callback(x,
                                                                                        e)
                                                                          .WaitSync()),
                   ValidateType.Async => policyOrig.SetValidate(x => Callback(x)),
                   ValidateType.AsyncCancellation => policyOrig.SetValidate((x,
                                                                             ct) => Callback(x,
                                                                                             ct: ct)),
                   ValidateType.AsyncException => policyOrig.SetValidate((x,
                                                                          e) => Callback(x,
                                                                                         e)),
                   ValidateType.AsyncExceptionCancellation => policyOrig.SetValidate(Callback),
                   _                                       => throw new ArgumentOutOfRangeException(),
                 };

    Assert.That(policy,
                Is.SameAs(policyOrig));

    ValidateReleaseImpl(policy,
                        validateType,
                        throwType);
    return;

    ValueTask<bool> Callback(bool              x,
                             Exception?        e  = null,
                             CancellationToken ct = default)
      => ValueTaskFactory(e is null
                            ? !x
                            : x,
                          delay,
                          throwType,
                          ct);
  }

  [Test]
  [AbortAfter(1000)]
  [TestCase(null)]
  [TestCase(-1)]
  [TestCase(0)]
  [TestCase(1)]
  [TestCase(5)]
  public void MaxNumberOfInstances(int? n)
  {
    var policyOrig = new PoolPolicy<int>();

    var policy = n is null
                   ? policyOrig
                   : policyOrig.SetMaxNumberOfInstances(n.Value);

    Assert.That(policy.MaxNumberOfInstances,
                Is.EqualTo(n ?? -1));
  }

  [Test]
  [AbortAfter(1000)]
  public async Task CreateMethodGroupWithAndWithoutToken([Values] bool useAsync)
  {
    // A method group having a form with and a form without token must compile, and call the form with token
    var f      = new OverloadedFunc();
    var policy = new PoolPolicy<int>();

    _ = useAsync
          ? policy.SetCreate(f.CreateAsync)
          : policy.SetCreate(f.Create);

    using var cts = new CancellationTokenSource();

    Assert.That(await policy.CreateAsync(cts.Token)
                            .ConfigureAwait(false),
                Is.EqualTo(1));
    Assert.That(f.WithToken,
                Is.EqualTo(1));
    Assert.That(f.WithoutToken,
                Is.Zero);
    Assert.That(f.Token,
                Is.EqualTo(cts.Token));
  }

  [Test]
  [AbortAfter(1000)]
  public async Task ValidateMethodGroupWithAndWithoutToken([Values] ValidateSetter setter,
                                                           [Values] bool           useAsync,
                                                           [Values] bool           withException)
  {
    // A method group having a form with and a form without token must compile, and call the form with token
    var f      = new OverloadedFunc();
    var policy = new PoolPolicy<int>();

    _ = (setter, useAsync, withException) switch
        {
          (ValidateSetter.Acquire, false, false) => policy.SetValidateAcquire(f.Validate),
          (ValidateSetter.Acquire, false, true)  => policy.SetValidateAcquire(f.ValidateException),
          (ValidateSetter.Acquire, true, false)  => policy.SetValidateAcquire(f.ValidateAsync),
          (ValidateSetter.Acquire, true, true)   => policy.SetValidateAcquire(f.ValidateExceptionAsync),
          (ValidateSetter.Release, false, false) => policy.SetValidateRelease(f.Validate),
          (ValidateSetter.Release, false, true)  => policy.SetValidateRelease(f.ValidateException),
          (ValidateSetter.Release, true, false)  => policy.SetValidateRelease(f.ValidateAsync),
          (ValidateSetter.Release, true, true)   => policy.SetValidateRelease(f.ValidateExceptionAsync),
          (ValidateSetter.Both, false, false)    => policy.SetValidate(f.Validate),
          (ValidateSetter.Both, false, true)     => policy.SetValidate(f.ValidateException),
          (ValidateSetter.Both, true, false)     => policy.SetValidate(f.ValidateAsync),
          (ValidateSetter.Both, true, true)      => policy.SetValidate(f.ValidateExceptionAsync),
          _                                      => throw new ArgumentOutOfRangeException(nameof(setter)),
        };

    using var cts = new CancellationTokenSource();

    Assert.That(await policy.ValidateAcquireAsync(0,
                                                  cts.Token)
                            .ConfigureAwait(false),
                Is.True);
    Assert.That(await policy.ValidateReleaseAsync(0,
                                                  null,
                                                  cts.Token)
                            .ConfigureAwait(false),
                Is.True);

    Assert.That(f.WithToken,
                Is.EqualTo(setter is ValidateSetter.Both
                             ? 2
                             : 1));
    Assert.That(f.WithoutToken,
                Is.Zero);
    Assert.That(f.Token,
                Is.EqualTo(cts.Token));
  }

  private void ValidateAcquireImpl(PoolPolicy<bool> policy,
                                   ValidateType     validateType,
                                   ThrowType        throwType)
  {
    Func<IResolveConstraint?> throwConstraint = throwType is not ThrowType.DoNotThrow
                                                  ? Throws.InstanceOf<ApplicationException>
                                                  : () => null;

    AssertThatAsync(policy.ValidateAcquireAsync(false),
                    throwConstraint() ?? Is.True);
    AssertThatAsync(policy.ValidateAcquireAsync(true),
                    throwConstraint() ?? Is.False);


    AssertThatAsync(policy.ValidateAcquireAsync(false,
                                                new CancellationToken(false)),
                    throwConstraint() ?? Is.True);
    AssertThatAsync(policy.ValidateAcquireAsync(true,
                                                new CancellationToken(false)),
                    throwConstraint() ?? Is.False);


    var cancellationToken = new CancellationToken(true);
    Func<IResolveConstraint?> cancellationConstraint = validateType is ValidateType.AsyncCancellation or ValidateType.AsyncExceptionCancellation
                                                         ? () => Throws.InstanceOf<OperationCanceledException>()
                                                                       .And.Property(nameof(OperationCanceledException.CancellationToken))
                                                                       .EqualTo(cancellationToken)
                                                         : () => null;

    AssertThatAsync(policy.ValidateAcquireAsync(false,
                                                cancellationToken),
                    throwConstraint() ?? cancellationConstraint() ?? Is.True);
    AssertThatAsync(policy.ValidateAcquireAsync(true,
                                                cancellationToken),
                    throwConstraint() ?? cancellationConstraint() ?? Is.False);
  }

  private void ValidateReleaseImpl(PoolPolicy<bool> policy,
                                   ValidateType     validateType,
                                   ThrowType        throwType)
  {
    Func<IResolveConstraint?> throwConstraint = throwType is not ThrowType.DoNotThrow
                                                  ? Throws.InstanceOf<ApplicationException>
                                                  : () => null;
    var handlesException = validateType is ValidateType.SyncException or ValidateType.AsyncException or ValidateType.AsyncExceptionCancellation;

    AssertThatAsync(policy.ValidateReleaseAsync(false,
                                                null),
                    throwConstraint() ?? Is.True);
    AssertThatAsync(policy.ValidateReleaseAsync(true,
                                                null),
                    throwConstraint() ?? Is.False);
    AssertThatAsync(policy.ValidateReleaseAsync(false,
                                                new NotImplementedException()),
                    throwConstraint() ?? Is.EqualTo(!handlesException));
    AssertThatAsync(policy.ValidateReleaseAsync(true,
                                                new NotImplementedException()),
                    throwConstraint() ?? Is.EqualTo(handlesException));

    AssertThatAsync(policy.ValidateReleaseAsync(false,
                                                null,
                                                new CancellationToken(false)),
                    throwConstraint() ?? Is.True);
    AssertThatAsync(policy.ValidateReleaseAsync(true,
                                                null,
                                                new CancellationToken(false)),
                    throwConstraint() ?? Is.False);
    AssertThatAsync(policy.ValidateReleaseAsync(false,
                                                new NotImplementedException(),
                                                new CancellationToken(false)),
                    throwConstraint() ?? Is.EqualTo(!handlesException));
    AssertThatAsync(policy.ValidateReleaseAsync(true,
                                                new NotImplementedException(),
                                                new CancellationToken(false)),
                    throwConstraint() ?? Is.EqualTo(handlesException));


    var cancellationToken = new CancellationToken(true);
    Func<IResolveConstraint?> cancellationConstraint = validateType is ValidateType.AsyncCancellation or ValidateType.AsyncExceptionCancellation
                                                         ? () => Throws.InstanceOf<OperationCanceledException>()
                                                                       .And.Property(nameof(OperationCanceledException.CancellationToken))
                                                                       .EqualTo(cancellationToken)
                                                         : () => null;

    AssertThatAsync(policy.ValidateReleaseAsync(false,
                                                null,
                                                cancellationToken),
                    throwConstraint() ?? cancellationConstraint() ?? Is.True);
    AssertThatAsync(policy.ValidateReleaseAsync(true,
                                                null,
                                                cancellationToken),
                    throwConstraint() ?? cancellationConstraint() ?? Is.False);
    AssertThatAsync(policy.ValidateReleaseAsync(false,
                                                new NotImplementedException(),
                                                cancellationToken),
                    throwConstraint() ?? cancellationConstraint() ?? Is.EqualTo(!handlesException));
    AssertThatAsync(policy.ValidateReleaseAsync(true,
                                                new NotImplementedException(),
                                                cancellationToken),
                    throwConstraint() ?? cancellationConstraint() ?? Is.EqualTo(handlesException));
  }

  private static void AssertThatAsync<T>(ValueTask<T>       task,
                                         IResolveConstraint expr)
    => Assert.That(() => task,
                   expr);

  private static ValueTask<T> ValueTaskFactory<T>(T                 value,
                                                  int?              delay             = 0,
                                                  ThrowType         throwType         = ThrowType.DoNotThrow,
                                                  CancellationToken cancellationToken = default)
  {
    if (throwType is ThrowType.ThrowSync)
    {
      throw new ApplicationException();
    }

    return Core();

    async ValueTask<T> Core()
    {
      if (delay is not null)
      {
        await Task.Delay(delay.Value,
                         CancellationToken.None);
      }
      else
      {
        await Task.Yield();
      }

      if (throwType is ThrowType.ThrowAsync)
      {
        throw new ApplicationException();
      }

      cancellationToken.ThrowIfCancellationRequested();


      return value;
    }
  }

  /// <summary>
  ///   Methods having a form with and a form without token
  /// </summary>
  private sealed class OverloadedFunc
  {
    public CancellationToken Token;
    public int               WithToken;
    public int               WithoutToken;

    public int Create()
    {
      WithoutToken++;
      return 1;
    }

    public ValueTask<int> Create(CancellationToken cancellationToken)
      => WithTokenAsync(1,
                        cancellationToken);

    public ValueTask<int> CreateAsync()
    {
      WithoutToken++;
      return new ValueTask<int>(1);
    }

    public ValueTask<int> CreateAsync(CancellationToken cancellationToken)
      => WithTokenAsync(1,
                        cancellationToken);

    public bool Validate(int x)
    {
      WithoutToken++;
      return true;
    }

    public ValueTask<bool> Validate(int               x,
                                    CancellationToken cancellationToken)
      => WithTokenAsync(true,
                        cancellationToken);

    public ValueTask<bool> ValidateAsync(int x)
    {
      WithoutToken++;
      return new ValueTask<bool>(true);
    }

    public ValueTask<bool> ValidateAsync(int               x,
                                         CancellationToken cancellationToken)
      => WithTokenAsync(true,
                        cancellationToken);

    public bool ValidateException(int        x,
                                  Exception? e)
    {
      WithoutToken++;
      return true;
    }

    public ValueTask<bool> ValidateException(int               x,
                                             Exception?        e,
                                             CancellationToken cancellationToken)
      => WithTokenAsync(true,
                        cancellationToken);

    public ValueTask<bool> ValidateExceptionAsync(int        x,
                                                  Exception? e)
    {
      WithoutToken++;
      return new ValueTask<bool>(true);
    }

    public ValueTask<bool> ValidateExceptionAsync(int               x,
                                                  Exception?        e,
                                                  CancellationToken cancellationToken)
      => WithTokenAsync(true,
                        cancellationToken);

    private ValueTask<TResult> WithTokenAsync<TResult>(TResult           result,
                                                       CancellationToken cancellationToken)
    {
      WithToken++;
      Token = cancellationToken;
      return new ValueTask<TResult>(result);
    }
  }
}

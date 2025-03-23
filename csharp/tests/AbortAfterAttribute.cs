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

using NUnit.Framework;
using NUnit.Framework.Interfaces;
using NUnit.Framework.Internal;
using NUnit.Framework.Internal.Commands;

namespace ArmoniK.Utils.Tests;

[AttributeUsage(AttributeTargets.Method,
                Inherited = false)]
public class AbortAfterAttribute(int timeout) : PropertyAttribute, IWrapTestMethod
{
  public TestCommand Wrap(TestCommand command)
    => new Command(timeout,
                   command);

  private class Command(int         timeout,
                        TestCommand innerCommand) : DelegatingTestCommand(innerCommand)
  {
    public override TestResult Execute(TestExecutionContext context)
    {
      var cts = new CancellationTokenSource(timeout);
      try
      {
        return Task.WhenAny(cts.Token.AsTask<TestResult>(),
                            Task.Run(() => innerCommand.Execute(context),
                                     cts.Token))
                   .Unwrap()
                   .WaitSync();
      }
      catch (OperationCanceledException) when (cts.Token.IsCancellationRequested)
      {
        context.StopOnError = true;

        context.CurrentResult.SetResult(ResultState.Failure,
                                        $"Test ran for more than {timeout} ms.");
        context.Dispatcher.CancelRun(false);
        return context.CurrentResult;
      }
    }
  }
}

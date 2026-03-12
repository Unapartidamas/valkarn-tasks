// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

using System;
using System.Threading;
using NUnit.Framework;

namespace UnaPartidaMas.Valkarn.Tasks.Tests
{
    /// <summary>
    /// Common setup/teardown for all Valkarn Tasks tests.
    /// Ensures ValkarnTaskPoolShared has the correct main thread ID
    /// and cleans up UnobservedException handlers between tests.
    /// </summary>
    internal static class TestHelper
    {
        static bool s_initialized;

        internal static void EnsureInitialized()
        {
            if (!s_initialized)
            {
                ValkarnTaskPoolShared.SetMainThreadId(Thread.CurrentThread.ManagedThreadId);
                s_initialized = true;
            }
        }

        /// <summary>
        /// Collects exceptions published via ValkarnTask.UnobservedException during a test.
        /// Dispose to unsubscribe.
        /// </summary>
        internal sealed class UnobservedExceptionCollector : IDisposable
        {
            readonly Action<Exception> handler;
            public System.Collections.Generic.List<Exception> Exceptions { get; } = new();

            public UnobservedExceptionCollector()
            {
                handler = ex => Exceptions.Add(ex);
                ValkarnTask.UnobservedException += handler;
            }

            public void Dispose()
            {
                ValkarnTask.UnobservedException -= handler;
            }
        }

        /// <summary>
        /// Runs a synchronously-completing async ValkarnTask and asserts success.
        /// Only works if the task completes without suspension (all awaited inputs are sync-completed).
        /// </summary>
        internal static void RunSync(ValkarnTask task)
        {
            Assert.IsTrue(task.IsCompleted, "Task was expected to complete synchronously but is still pending.");
            task.GetAwaiter().GetResult();
        }

        /// <summary>
        /// Runs a synchronously-completing async ValkarnTask{T} and returns the result.
        /// </summary>
        internal static T RunSync<T>(ValkarnTask<T> task)
        {
            Assert.IsTrue(task.IsCompleted, "Task was expected to complete synchronously but is still pending.");
            return task.GetAwaiter().GetResult();
        }
    }
}

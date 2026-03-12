// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

using System;
using NUnit.Framework;

namespace UnaPartidaMas.Valkarn.Tasks.Tests
{
    [TestFixture]
    public class WhenAllTypedTests
    {
        [SetUp]
        public void SetUp()
        {
            TestHelper.EnsureInitialized();
        }

        // ── 2-tuple typed ──

        [Test]
        public void WhenAll_2Tuple_BothSucceed()
        {
            var t1 = ValkarnTask.FromResult(1);
            var t2 = ValkarnTask.FromResult("hello");

            var (r1, r2) = TestHelper.RunSync(ValkarnTask.WhenAll(t1, t2));

            Assert.AreEqual(1, r1);
            Assert.AreEqual("hello", r2);
        }

        [Test]
        public void WhenAll_2Tuple_FirstFaults_Throws()
        {
            var ex = new Exception("fail");
            var t1 = ValkarnTask.FromException<int>(ex);
            var t2 = ValkarnTask.FromResult("ok");

            var thrown = Assert.Throws<Exception>(() =>
                TestHelper.RunSync(ValkarnTask.WhenAll(t1, t2)));
            Assert.AreSame(ex, thrown);
        }

        [Test]
        public void WhenAll_2Tuple_SecondCanceled_Throws()
        {
            var t1 = ValkarnTask.FromResult(1);
            var t2 = ValkarnTask.FromCanceled<string>();

            Assert.Throws<OperationCanceledException>(() =>
                TestHelper.RunSync(ValkarnTask.WhenAll(t1, t2)));
        }

        [Test]
        public void WhenAll_2Tuple_BothFault_ThrowsFirst()
        {
            var ex1 = new Exception("a");
            var ex2 = new Exception("b");
            var t1 = ValkarnTask.FromException<int>(ex1);
            var t2 = ValkarnTask.FromException<int>(ex2);

            // First exception wins (ex1 — first in Interlocked.CompareExchange)
            var thrown = Assert.Throws<Exception>(() =>
                TestHelper.RunSync(ValkarnTask.WhenAll(t1, t2)));
            Assert.AreSame(ex1, thrown);
        }

        // ── IEnumerable<ValkarnTask<T>> ──

        [Test]
        public void WhenAll_Enumerable_AllSucceed()
        {
            var tasks = new[]
            {
                ValkarnTask.FromResult(1),
                ValkarnTask.FromResult(2),
                ValkarnTask.FromResult(3)
            };

            var results = TestHelper.RunSync(ValkarnTask.WhenAll<int>(tasks));

            Assert.AreEqual(3, results.Length);
            Assert.AreEqual(1, results[0]);
            Assert.AreEqual(2, results[1]);
            Assert.AreEqual(3, results[2]);
        }

        [Test]
        public void WhenAll_Enumerable_OneFaults_Throws()
        {
            var ex = new Exception("fail");
            var tasks = new[]
            {
                ValkarnTask.FromResult(1),
                ValkarnTask.FromException<int>(ex),
                ValkarnTask.FromResult(3)
            };

            var thrown = Assert.Throws<Exception>(() =>
                TestHelper.RunSync(ValkarnTask.WhenAll<int>(tasks)));
            Assert.AreSame(ex, thrown);
        }

        [Test]
        public void WhenAll_Enumerable_OneCanceled_Throws()
        {
            var tasks = new[]
            {
                ValkarnTask.FromResult(1),
                ValkarnTask.FromCanceled<int>(),
                ValkarnTask.FromResult(3)
            };

            Assert.Throws<OperationCanceledException>(() =>
                TestHelper.RunSync(ValkarnTask.WhenAll<int>(tasks)));
        }

        [Test]
        public void WhenAll_Enumerable_Empty()
        {
            var tasks = Array.Empty<ValkarnTask<int>>();
            var results = TestHelper.RunSync(ValkarnTask.WhenAll<int>(tasks));
            Assert.AreEqual(0, results.Length);
        }

        [Test]
        public void WhenAll_Enumerable_SingleTask()
        {
            var tasks = new[] { ValkarnTask.FromResult(42) };
            var results = TestHelper.RunSync(ValkarnTask.WhenAll<int>(tasks));

            Assert.AreEqual(1, results.Length);
            Assert.AreEqual(42, results[0]);
        }
    }

    [TestFixture]
    public class WhenAllNonGenericTests
    {
        [SetUp]
        public void SetUp()
        {
            TestHelper.EnsureInitialized();
        }

        // ── 2-tuple void ──

        [Test]
        public void WhenAll_2Tasks_BothSucceed()
        {
            var task = ValkarnTask.WhenAll(ValkarnTask.CompletedTask, ValkarnTask.CompletedTask);
            Assert.DoesNotThrow(() => TestHelper.RunSync(task));
        }

        [Test]
        public void WhenAll_2Tasks_OneFaults_Throws()
        {
            var ex = new Exception("err");
            var task = ValkarnTask.WhenAll(ValkarnTask.CompletedTask, ValkarnTask.FromException(ex));

            var thrown = Assert.Throws<Exception>(() => TestHelper.RunSync(task));
            Assert.AreSame(ex, thrown);
        }

        [Test]
        public void WhenAll_2Tasks_OneCanceled_Throws()
        {
            var task = ValkarnTask.WhenAll(ValkarnTask.FromCanceled(), ValkarnTask.CompletedTask);

            Assert.Throws<OperationCanceledException>(() => TestHelper.RunSync(task));
        }

        // ── 3-tuple void ──

        [Test]
        public void WhenAll_3Tasks_AllSucceed()
        {
            var task = ValkarnTask.WhenAll(
                ValkarnTask.CompletedTask, ValkarnTask.CompletedTask, ValkarnTask.CompletedTask);
            Assert.DoesNotThrow(() => TestHelper.RunSync(task));
        }

        [Test]
        public void WhenAll_3Tasks_OneFaults_Throws()
        {
            var ex = new Exception("err");
            var task = ValkarnTask.WhenAll(
                ValkarnTask.CompletedTask, ValkarnTask.FromException(ex), ValkarnTask.CompletedTask);

            var thrown = Assert.Throws<Exception>(() => TestHelper.RunSync(task));
            Assert.AreSame(ex, thrown);
        }

        // ── IEnumerable<ValkarnTask> ──

        [Test]
        public void WhenAll_Enumerable_AllSucceed()
        {
            var tasks = new[] { ValkarnTask.CompletedTask, ValkarnTask.CompletedTask };
            var task = ValkarnTask.WhenAll((System.Collections.Generic.IEnumerable<ValkarnTask>)tasks);
            Assert.DoesNotThrow(() => TestHelper.RunSync(task));
        }

        [Test]
        public void WhenAll_Enumerable_Empty()
        {
            var tasks = Array.Empty<ValkarnTask>();
            var task = ValkarnTask.WhenAll((System.Collections.Generic.IEnumerable<ValkarnTask>)tasks);
            Assert.DoesNotThrow(() => TestHelper.RunSync(task));
        }

        [Test]
        public void WhenAll_Enumerable_OneFaults_Throws()
        {
            var ex = new Exception("err");
            var tasks = new[] { ValkarnTask.CompletedTask, ValkarnTask.FromException(ex) };
            var task = ValkarnTask.WhenAll((System.Collections.Generic.IEnumerable<ValkarnTask>)tasks);

            var thrown = Assert.Throws<Exception>(() => TestHelper.RunSync(task));
            Assert.AreSame(ex, thrown);
        }
    }
}

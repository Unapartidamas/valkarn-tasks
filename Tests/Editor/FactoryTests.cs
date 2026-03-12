// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

using System;
using System.Threading;
using NUnit.Framework;

namespace UnaPartidaMas.Valkarn.Tasks.Tests
{
    [TestFixture]
    public class FactoryTests
    {
        [SetUp]
        public void SetUp()
        {
            TestHelper.EnsureInitialized();
        }

        // ── FromException ──

        [Test]
        public void FromException_NonGeneric_IsFaulted()
        {
            var ex = new InvalidOperationException("test");
            var task = ValkarnTask.FromException(ex);

            Assert.IsTrue(task.IsCompleted);
            Assert.AreEqual(ValkarnTask.Status.Faulted, task.GetStatus());
        }

        [Test]
        public void FromException_NonGeneric_ThrowsOnGetResult()
        {
            var ex = new InvalidOperationException("boom");
            var task = ValkarnTask.FromException(ex);

            var thrown = Assert.Throws<InvalidOperationException>(() => task.GetAwaiter().GetResult());
            Assert.AreSame(ex, thrown);
        }

        [Test]
        public void FromException_Generic_IsFaulted()
        {
            var ex = new Exception("err");
            var task = ValkarnTask.FromException<int>(ex);

            Assert.IsTrue(task.IsCompleted);
            Assert.AreEqual(ValkarnTask.Status.Faulted, task.GetStatus());
        }

        [Test]
        public void FromException_Generic_ThrowsOnGetResult()
        {
            var ex = new InvalidOperationException("typed");
            var task = ValkarnTask.FromException<string>(ex);

            var thrown = Assert.Throws<InvalidOperationException>(() => task.GetAwaiter().GetResult());
            Assert.AreSame(ex, thrown);
        }

        // ── FromCanceled ──

        [Test]
        public void FromCanceled_NonGeneric_IsCanceled()
        {
            var task = ValkarnTask.FromCanceled();

            Assert.IsTrue(task.IsCompleted);
            Assert.AreEqual(ValkarnTask.Status.Canceled, task.GetStatus());
        }

        [Test]
        public void FromCanceled_NonGeneric_ThrowsOperationCanceled()
        {
            var task = ValkarnTask.FromCanceled();
            Assert.Throws<OperationCanceledException>(() => task.GetAwaiter().GetResult());
        }

        [Test]
        public void FromCanceled_WithToken_PreservesToken()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var task = ValkarnTask.FromCanceled(cts.Token);

            var thrown = Assert.Throws<OperationCanceledException>(() => task.GetAwaiter().GetResult());
            Assert.AreEqual(cts.Token, thrown.CancellationToken);
        }

        [Test]
        public void FromCanceled_Generic_IsCanceled()
        {
            var task = ValkarnTask.FromCanceled<int>();

            Assert.IsTrue(task.IsCompleted);
            Assert.AreEqual(ValkarnTask.Status.Canceled, task.GetStatus());
        }

        [Test]
        public void FromCanceled_Generic_ThrowsOperationCanceled()
        {
            var task = ValkarnTask.FromCanceled<int>();
            Assert.Throws<OperationCanceledException>(() => task.GetAwaiter().GetResult());
        }

        // ── Never ──

        [Test]
        public void Never_IsNotCompleted()
        {
            Assert.IsFalse(ValkarnTask.Never.IsCompleted);
        }

        [Test]
        public void Never_StatusIsPending()
        {
            Assert.AreEqual(ValkarnTask.Status.Pending, ValkarnTask.Never.GetStatus());
        }

        [Test]
        public void Never_SourceIsNotNull()
        {
            Assert.IsNotNull(ValkarnTask.Never.source);
        }
    }
}

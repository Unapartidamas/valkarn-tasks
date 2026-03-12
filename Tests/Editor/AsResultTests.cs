// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

using System;
using System.Threading;
using NUnit.Framework;

namespace UnaPartidaMas.Valkarn.Tasks.Tests
{
    [TestFixture]
    public class AsResultGenericTests
    {
        [SetUp]
        public void SetUp()
        {
            TestHelper.EnsureInitialized();
        }

        [Test]
        public void Success_WrapsValue()
        {
            var task = ValkarnTask.FromResult(42);
            var resultTask = task.AsResult();

            var result = TestHelper.RunSync(resultTask);
            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual(42, result.Value);
        }

        [Test]
        public void Faulted_WrapsException()
        {
            var ex = new InvalidOperationException("err");
            var task = ValkarnTask.FromException<int>(ex);
            var resultTask = task.AsResult();

            var result = TestHelper.RunSync(resultTask);
            Assert.IsTrue(result.IsFaulted);
            Assert.AreSame(ex, result.Error);
        }

        [Test]
        public void Canceled_WrapsCancellation()
        {
            var task = ValkarnTask.FromCanceled<int>();
            var resultTask = task.AsResult();

            var result = TestHelper.RunSync(resultTask);
            Assert.IsTrue(result.IsCanceled);
        }

        [Test]
        public void Success_NullValue_WrapsCorrectly()
        {
            var task = ValkarnTask.FromResult<string>(null);
            var resultTask = task.AsResult();

            var result = TestHelper.RunSync(resultTask);
            Assert.IsTrue(result.IsSuccess);
            Assert.IsNull(result.Value);
        }
    }

    [TestFixture]
    public class AsResultNonGenericTests
    {
        [SetUp]
        public void SetUp()
        {
            TestHelper.EnsureInitialized();
        }

        [Test]
        public void Success_WrapsAsSuccess()
        {
            var resultTask = ValkarnTask.CompletedTask.AsResult();

            var result = TestHelper.RunSync(resultTask);
            Assert.IsTrue(result.IsSuccess);
        }

        [Test]
        public void Faulted_WrapsException()
        {
            var ex = new InvalidOperationException("err");
            var task = ValkarnTask.FromException(ex);
            var resultTask = task.AsResult();

            var result = TestHelper.RunSync(resultTask);
            Assert.IsTrue(result.IsFaulted);
            Assert.AreSame(ex, result.Error);
        }

        [Test]
        public void Canceled_WrapsCancellation()
        {
            var task = ValkarnTask.FromCanceled();
            var resultTask = task.AsResult();

            var result = TestHelper.RunSync(resultTask);
            Assert.IsTrue(result.IsCanceled);
        }
    }
}

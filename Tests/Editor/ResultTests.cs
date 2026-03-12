// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

using System;
using NUnit.Framework;

namespace UnaPartidaMas.Valkarn.Tasks.Tests
{
    [TestFixture]
    public class ResultGenericTests
    {
        [Test]
        public void Success_HasCorrectProperties()
        {
            var result = Result<int>.Success(42);

            Assert.IsTrue(result.IsSuccess);
            Assert.IsFalse(result.IsFaulted);
            Assert.IsFalse(result.IsCanceled);
            Assert.AreEqual(ValkarnTask.Status.Succeeded, result.Status);
            Assert.AreEqual(42, result.Value);
            Assert.IsNull(result.Error);
        }

        [Test]
        public void Faulted_HasCorrectProperties()
        {
            var ex = new InvalidOperationException("test error");
            var result = Result<int>.Faulted(ex);

            Assert.IsFalse(result.IsSuccess);
            Assert.IsTrue(result.IsFaulted);
            Assert.IsFalse(result.IsCanceled);
            Assert.AreEqual(ValkarnTask.Status.Faulted, result.Status);
            Assert.AreSame(ex, result.Error);
        }

        [Test]
        public void Faulted_ValueThrows_OriginalException()
        {
            var ex = new Exception("err");
            var result = Result<int>.Faulted(ex);
            // ThrowResultNotSucceeded re-throws the stored exception
            var thrown = Assert.Throws<Exception>(() => { var _ = result.Value; });
            Assert.AreSame(ex, thrown);
        }

        [Test]
        public void Canceled_HasCorrectProperties()
        {
            var result = Result<int>.Canceled();

            Assert.IsFalse(result.IsSuccess);
            Assert.IsFalse(result.IsFaulted);
            Assert.IsTrue(result.IsCanceled);
            Assert.AreEqual(ValkarnTask.Status.Canceled, result.Status);
        }

        [Test]
        public void Canceled_ValueThrows_OperationCanceledException()
        {
            var result = Result<int>.Canceled();
            // ThrowResultNotSucceeded re-throws the stored OCE (or creates a new one)
            Assert.Throws<OperationCanceledException>(() => { var _ = result.Value; });
        }

        [Test]
        public void ImplicitBool_TrueOnSuccess()
        {
            var result = Result<int>.Success(1);
            bool b = result;
            Assert.IsTrue(b);
        }

        [Test]
        public void ImplicitBool_FalseOnFault()
        {
            var result = Result<int>.Faulted(new Exception());
            bool b = result;
            Assert.IsFalse(b);
        }

        [Test]
        public void ImplicitBool_FalseOnCanceled()
        {
            var result = Result<int>.Canceled();
            bool b = result;
            Assert.IsFalse(b);
        }

        [Test]
        public void Success_NullValue_IsValid()
        {
            var result = Result<string>.Success(null);
            Assert.IsTrue(result.IsSuccess);
            Assert.IsNull(result.Value);
        }

        [Test]
        public void ToString_ReportsCorrectly()
        {
            Assert.That(Result<int>.Success(42).ToString(), Does.Contain("42"));
            Assert.That(Result<int>.Faulted(new Exception("boom")).ToString(), Does.Contain("boom"));
            Assert.AreEqual("Canceled", Result<int>.Canceled().ToString());
        }
    }

    [TestFixture]
    public class ResultNonGenericTests
    {
        [Test]
        public void SuccessResult_HasCorrectProperties()
        {
            var result = Result.SuccessResult;

            Assert.IsTrue(result.IsSuccess);
            Assert.IsFalse(result.IsFaulted);
            Assert.IsFalse(result.IsCanceled);
            Assert.AreEqual(ValkarnTask.Status.Succeeded, result.Status);
            Assert.IsNull(result.Error);
        }

        [Test]
        public void Faulted_HasCorrectProperties()
        {
            var ex = new InvalidOperationException("err");
            var result = Result.Faulted(ex);

            Assert.IsFalse(result.IsSuccess);
            Assert.IsTrue(result.IsFaulted);
            Assert.AreSame(ex, result.Error);
        }

        [Test]
        public void Canceled_HasCorrectProperties()
        {
            var result = Result.Canceled();

            Assert.IsFalse(result.IsSuccess);
            Assert.IsTrue(result.IsCanceled);
        }

        [Test]
        public void ImplicitBool_MatchesSucceeded()
        {
            Assert.IsTrue((bool)Result.SuccessResult);
            Assert.IsFalse((bool)Result.Faulted(new Exception()));
            Assert.IsFalse((bool)Result.Canceled());
        }
    }
}

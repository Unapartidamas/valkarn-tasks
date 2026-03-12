// Copyright (c) 2025 Una Partida Mas. All rights reserved.
// Licensed under the Valkarn Tasks License. See LICENSE.md in the repository root.

using Microsoft.CodeAnalysis;

namespace UnaPartidaMas.Valkarn.Tasks.Analyzer
{
    internal static class MigrationDescriptors
    {
        const string Category = "ValkarnTaskMigration";

        // ── UniTask → ValkarnTask ──

        public static readonly DiagnosticDescriptor UniTaskType = new(
            "MIG001",
            "UniTask type detected",
            "'{0}' can be replaced with ValkarnTask equivalent",
            Category, DiagnosticSeverity.Info, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor CancelImmediately = new(
            "MIG002",
            "cancelImmediately parameter is unnecessary",
            "ValkarnTask cancels immediately by default; the 'cancelImmediately' parameter can be removed",
            Category, DiagnosticSeverity.Info, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor SuppressCancellationThrow = new(
            "MIG003",
            "SuppressCancellationThrow() detected",
            "Replace SuppressCancellationThrow() with AsResult() for safe cancellation handling",
            Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor AwaitableType = new(
            "MIG004",
            "Awaitable return type detected",
            "'{0}' return type can be replaced with ValkarnTask",
            Category, DiagnosticSeverity.Info, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor UnboundedChannel = new(
            "MIG005",
            "SingleConsumerUnbounded channel detected",
            "Consider using bounded channel for backpressure via ValkarnTask.Channel.CreateBounded<T>(capacity)",
            Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor PlayerLoopTimingNamespace = new(
            "MIG006",
            "UniTask PlayerLoopTiming detected",
            "Change namespace from Cysharp.Threading.Tasks to UnaPartidaMas.Valkarn.Tasks; enum values are compatible",
            Category, DiagnosticSeverity.Info, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor UniTaskVoid = new(
            "MIG007",
            "async UniTaskVoid detected",
            "Replace async UniTaskVoid with [FireAndForget] async ValkarnTask or use Forget()",
            Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor AwaitableMainThread = new(
            "MIG008",
            "Awaitable MainThreadAsync() detected",
            "ValkarnTask runs on main thread by default; MainThreadAsync() call can be removed",
            Category, DiagnosticSeverity.Info, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor RunOnThreadPool = new(
            "MIG009",
            "UniTask.RunOnThreadPool detected",
            "Replace UniTask.RunOnThreadPool(func) with ValkarnTask.RunOnThreadPool(func)",
            Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor ToCoroutine = new(
            "MIG010",
            ".ToCoroutine() detected",
            "Rewrite coroutine as async ValkarnTask method instead of using ToCoroutine()",
            Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor UniTaskCreate = new(
            "MIG011",
            "UniTask.Create() detected",
            "Replace UniTask.Create(func) with ValkarnTask.Promise<T> pattern",
            Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor LazyDefer = new(
            "MIG012",
            "UniTask.Lazy/Defer detected",
            "UniTask.Lazy<T> and UniTask.Defer are unnecessary with Valkarn's zero-alloc sync fast path",
            Category, DiagnosticSeverity.Info, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor ToUniTask = new(
            "MIG013",
            ".ToUniTask()/.AsUniTask() detected",
            "Remove conversion call; ValkarnTask bridges Awaitable natively",
            Category, DiagnosticSeverity.Info, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor AsyncEnumerable = new(
            "MIG014",
            "UniTaskAsyncEnumerable detected",
            "Use IAsyncEnumerable<T> with System.Linq.Async instead of UniTaskAsyncEnumerable",
            Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor TimeoutController = new(
            "MIG015",
            "TimeoutController detected",
            "Replace TimeoutController with 'new CancellationTokenSource(TimeSpan)'",
            Category, DiagnosticSeverity.Info, isEnabledByDefault: true);
    }
}

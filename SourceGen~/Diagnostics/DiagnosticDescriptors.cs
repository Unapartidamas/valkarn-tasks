// Copyright (c) 2025 Una Partida Mas. All rights reserved.
// Licensed under the Valkarn Tasks License. See LICENSE.md in the repository root.

using Microsoft.CodeAnalysis;

namespace UnaPartidaMas.Valkarn.Tasks.SourceGen
{
    internal static class DiagnosticDescriptors
    {
        const string Category = "ValkarnTask";

        // ── Errors ──

        public static readonly DiagnosticDescriptor DoubleAwait = new(
            "TT001",
            "ValkarnTask already awaited",
            "Cannot await a ValkarnTask that was already awaited. ValkarnTask is single-consumption; assign to a new variable or use .AsResult() if branching is needed.",
            Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor UnawaitedTask = new(
            "TT002",
            "ValkarnTask not awaited or discarded",
            "async ValkarnTask method '{0}' must be awaited or explicitly discarded with .Forget(). Unawaited tasks may silently swallow exceptions.",
            Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

        // ── Informational ──

        public static readonly DiagnosticDescriptor AutoCancelActive = new(
            "TT010",
            "Auto-cancel active for MonoBehaviour async method",
            "Async method '{0}' in MonoBehaviour '{1}' will be auto-cancelled on Destroy. Add [NoAutoCancel] to opt out.",
            Category, DiagnosticSeverity.Info, isEnabledByDefault: true);

        // ── Warnings ──

        public static readonly DiagnosticDescriptor MixedLifetimeWhenAll = new(
            "TT011",
            "WhenAll mixes different lifetime scopes",
            "WhenAll contains tasks with different lifetimes. Task '{0}' is bound to MonoBehaviour, task '{1}' is unbound.",
            Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor ZombieLoop = new(
            "TT012",
            "Async loop without cancellation check",
            "Async loop in '{0}' has no cancellation check. If lifecycle cancellation triggers, this loop becomes a zombie.",
            Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor ReturnedTaskNotConsumed = new(
            "TT013",
            "ValkarnTask returned but never consumed",
            "ValkarnTask returned by '{0}' is never awaited and not explicitly discarded. Potential fire-and-forget bug.",
            Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor NoAutoCancelWithoutToken = new(
            "TT014",
            "[NoAutoCancel] without CancellationToken parameter",
            "[NoAutoCancel] on async method '{0}' in MonoBehaviour '{1}' without a CancellationToken parameter. The method cannot be cancelled.",
            Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor AwaitableBridgeGenerated = new(
            "TT015",
            "Awaitable bridge adapter generated",
            "Awaiting Unity Awaitable inside ValkarnTask. Bridge adapter generated automatically.",
            Category, DiagnosticSeverity.Info, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor AsyncWithoutAwait = new(
            "TT016",
            "Async method without await",
            "Async method '{0}' contains no await expressions. Consider making it synchronous or returning ValkarnTask.CompletedTask / ValkarnTask.FromResult().",
            Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor FireAndForgetOnTyped = new(
            "TT017",
            "[FireAndForget] on ValkarnTask<T>",
            "[FireAndForget] on method '{0}' returning ValkarnTask<T>. Fire-and-forget discards the return value. Use ValkarnTask (void) instead.",
            Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);
    }
}

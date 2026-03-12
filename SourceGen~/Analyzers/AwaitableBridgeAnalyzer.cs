// Copyright (c) 2025 Una Partida Mas. All rights reserved.
// Licensed under the Valkarn Tasks License. See LICENSE.md in the repository root.

using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace UnaPartidaMas.Valkarn.Tasks.SourceGen.Analyzers
{
    /// <summary>
    /// Reports TT015 (info) when an async ValkarnTask method awaits a Unity Awaitable.
    /// This is informational — the bridge adapter in AwaitableBridge.cs handles the
    /// conversion automatically. This diagnostic lets the developer know it's happening.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class AwaitableBridgeAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
            => ImmutableArray.Create(DiagnosticDescriptors.AwaitableBridgeGenerated);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxNodeAction(AnalyzeAwait, SyntaxKind.AwaitExpression);
        }

        static void AnalyzeAwait(SyntaxNodeAnalysisContext context)
        {
            var awaitExpr = (AwaitExpressionSyntax)context.Node;

            // Check the type of the awaited expression
            var typeInfo = context.SemanticModel.GetTypeInfo(awaitExpr.Expression, context.CancellationToken);
            if (!IsUnityAwaitable(typeInfo.Type))
                return;

            // Must be inside an async ValkarnTask method
            var method = awaitExpr.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
            if (method == null || !method.Modifiers.Any(SyntaxKind.AsyncKeyword))
                return;

            var returnType = context.SemanticModel.GetTypeInfo(method.ReturnType, context.CancellationToken).Type;
            if (!ValkarnTypeHelper.IsAnyValkarnTask(returnType))
                return;

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.AwaitableBridgeGenerated,
                awaitExpr.GetLocation()));
        }

        static bool IsUnityAwaitable(ITypeSymbol type)
        {
            if (type == null) return false;
            if (type.Name != "Awaitable") return false;
            return type.ContainingNamespace?.ToDisplayString() == "UnityEngine";
        }
    }
}

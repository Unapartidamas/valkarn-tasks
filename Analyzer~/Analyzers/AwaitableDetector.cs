// Copyright (c) 2025 Una Partida Mas. All rights reserved.
// Licensed under the Valkarn Tasks License. See LICENSE.md in the repository root.

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace UnaPartidaMas.Valkarn.Tasks.Analyzer.Analyzers
{
    /// <summary>
    /// Detects Unity Awaitable types and methods that should migrate to ValkarnTask.
    /// Covers MIG004 (Awaitable type), MIG008 (MainThreadAsync).
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class AwaitableDetector : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
            => ImmutableArray.Create(
                MigrationDescriptors.AwaitableType,
                MigrationDescriptors.AwaitableMainThread);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();

            context.RegisterCompilationStartAction(compilationCtx =>
            {
                var awaitableType = compilationCtx.Compilation.GetTypeByMetadataName("UnityEngine.Awaitable");
                if (awaitableType == null) return;

                compilationCtx.RegisterSyntaxNodeAction(
                    ctx => AnalyzeMethodReturn(ctx, awaitableType),
                    SyntaxKind.MethodDeclaration);

                compilationCtx.RegisterSyntaxNodeAction(
                    ctx => AnalyzeVariableDeclaration(ctx, awaitableType),
                    SyntaxKind.VariableDeclaration);

                compilationCtx.RegisterSyntaxNodeAction(
                    ctx => AnalyzeParameter(ctx, awaitableType),
                    SyntaxKind.Parameter);

                compilationCtx.RegisterSyntaxNodeAction(
                    AnalyzeMainThreadAsync,
                    SyntaxKind.InvocationExpression);
            });
        }

        static void AnalyzeMethodReturn(SyntaxNodeAnalysisContext context, INamedTypeSymbol awaitableType)
        {
            var method = (MethodDeclarationSyntax)context.Node;
            var typeInfo = context.SemanticModel.GetTypeInfo(method.ReturnType, context.CancellationToken);
            if (typeInfo.Type == null) return;

            if (SymbolEqualityComparer.Default.Equals(typeInfo.Type.OriginalDefinition, awaitableType)
                || (typeInfo.Type is INamedTypeSymbol named
                    && named.ConstructedFrom != null
                    && named.ContainingNamespace?.ToDisplayString() == "UnityEngine"
                    && named.Name == "Awaitable"))
            {
                var displayName = typeInfo.Type is INamedTypeSymbol { IsGenericType: true } generic
                    ? $"Awaitable<{string.Join(", ", generic.TypeArguments)}>"
                    : "Awaitable";

                context.ReportDiagnostic(Diagnostic.Create(
                    MigrationDescriptors.AwaitableType,
                    method.ReturnType.GetLocation(),
                    displayName));
            }
        }

        static void AnalyzeVariableDeclaration(SyntaxNodeAnalysisContext context, INamedTypeSymbol awaitableType)
        {
            var decl = (VariableDeclarationSyntax)context.Node;
            var typeInfo = context.SemanticModel.GetTypeInfo(decl.Type, context.CancellationToken);
            CheckAwaitableType(context, decl.Type, typeInfo.Type, awaitableType);
        }

        static void AnalyzeParameter(SyntaxNodeAnalysisContext context, INamedTypeSymbol awaitableType)
        {
            var param = (ParameterSyntax)context.Node;
            if (param.Type == null) return;
            var typeInfo = context.SemanticModel.GetTypeInfo(param.Type, context.CancellationToken);
            CheckAwaitableType(context, param.Type, typeInfo.Type, awaitableType);
        }

        static void CheckAwaitableType(SyntaxNodeAnalysisContext context, SyntaxNode typeNode,
            ITypeSymbol type, INamedTypeSymbol awaitableType)
        {
            if (type == null) return;
            if (!SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, awaitableType)
                && !(type is INamedTypeSymbol named
                    && named.ConstructedFrom != null
                    && named.ContainingNamespace?.ToDisplayString() == "UnityEngine"
                    && named.Name == "Awaitable"))
                return;

            var displayName = type is INamedTypeSymbol { IsGenericType: true } generic
                ? $"Awaitable<{string.Join(", ", generic.TypeArguments)}>"
                : "Awaitable";

            context.ReportDiagnostic(Diagnostic.Create(
                MigrationDescriptors.AwaitableType,
                typeNode.GetLocation(),
                displayName));
        }

        static void AnalyzeMainThreadAsync(SyntaxNodeAnalysisContext context)
        {
            var invocation = (InvocationExpressionSyntax)context.Node;
            if (invocation.Expression is MemberAccessExpressionSyntax memberAccess
                && memberAccess.Name.Identifier.Text == "MainThreadAsync")
            {
                var symbolInfo = context.SemanticModel.GetSymbolInfo(memberAccess.Expression, context.CancellationToken);
                if (symbolInfo.Symbol is INamedTypeSymbol type
                    && (type.OriginalDefinition ?? type).Name == "Awaitable"
                    && (type.OriginalDefinition ?? type).ContainingNamespace?.ToDisplayString() == "UnityEngine")
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        MigrationDescriptors.AwaitableMainThread,
                        invocation.GetLocation()));
                }
            }
        }
    }
}

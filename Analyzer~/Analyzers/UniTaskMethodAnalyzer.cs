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
    /// Detects UniTask-specific method patterns that need migration.
    /// Covers MIG003 (SuppressCancellationThrow), MIG009 (RunOnThreadPool),
    /// MIG010 (ToCoroutine), MIG011 (UniTask.Create), MIG013 (ToUniTask/AsUniTask),
    /// MIG015 (TimeoutController).
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UniTaskMethodAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
            => ImmutableArray.Create(
                MigrationDescriptors.SuppressCancellationThrow,
                MigrationDescriptors.RunOnThreadPool,
                MigrationDescriptors.ToCoroutine,
                MigrationDescriptors.UniTaskCreate,
                MigrationDescriptors.ToUniTask,
                MigrationDescriptors.TimeoutController);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();

            context.RegisterCompilationStartAction(compilationCtx =>
            {
                // Use GetTypeByMetadataName: ProjectReferences in Rider
                // don't appear in ReferencedAssemblyNames.
                if (compilationCtx.Compilation.GetTypeByMetadataName("Cysharp.Threading.Tasks.UniTask") == null)
                    return;

                compilationCtx.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
                compilationCtx.RegisterSyntaxNodeAction(AnalyzeObjectCreation, SyntaxKind.ObjectCreationExpression);
            });
        }

        static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
        {
            var invocation = (InvocationExpressionSyntax)context.Node;

            string methodName = null;
            if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
                methodName = memberAccess.Name.Identifier.Text;
            else if (invocation.Expression is IdentifierNameSyntax identifier)
                methodName = identifier.Identifier.Text;

            if (methodName == null) return;

            switch (methodName)
            {
                case "SuppressCancellationThrow":
                    if (IsUniTaskInstanceCall(context, invocation))
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            MigrationDescriptors.SuppressCancellationThrow,
                            invocation.GetLocation()));
                    }
                    break;

                case "RunOnThreadPool":
                    if (IsUniTaskStaticCall(context, invocation))
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            MigrationDescriptors.RunOnThreadPool,
                            invocation.GetLocation()));
                    }
                    break;

                case "ToCoroutine":
                    if (IsUniTaskInstanceCall(context, invocation))
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            MigrationDescriptors.ToCoroutine,
                            invocation.GetLocation()));
                    }
                    break;

                case "Create":
                    if (IsUniTaskStaticCall(context, invocation))
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            MigrationDescriptors.UniTaskCreate,
                            invocation.GetLocation()));
                    }
                    break;

                case "ToUniTask":
                case "AsUniTask":
                    if (IsUniTaskInstanceCall(context, invocation))
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            MigrationDescriptors.ToUniTask,
                            invocation.GetLocation()));
                    }
                    break;
            }
        }

        static void AnalyzeObjectCreation(SyntaxNodeAnalysisContext context)
        {
            var creation = (ObjectCreationExpressionSyntax)context.Node;
            var typeInfo = context.SemanticModel.GetTypeInfo(creation, context.CancellationToken);
            if (typeInfo.Type == null) return;

            if (typeInfo.Type.Name == "TimeoutController"
                && typeInfo.Type.ContainingNamespace?.ToDisplayString() == "Cysharp.Threading.Tasks")
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    MigrationDescriptors.TimeoutController,
                    creation.GetLocation()));
            }
        }

        static bool IsUniTaskStaticCall(SyntaxNodeAnalysisContext context, InvocationExpressionSyntax invocation)
        {
            if (invocation.Expression is MemberAccessExpressionSyntax ma)
            {
                var symbolInfo = context.SemanticModel.GetSymbolInfo(ma.Expression, context.CancellationToken);
                if (symbolInfo.Symbol is INamedTypeSymbol type)
                    return type.Name == "UniTask" && type.ContainingNamespace?.ToDisplayString() == "Cysharp.Threading.Tasks";
            }
            return false;
        }

        static bool IsUniTaskInstanceCall(SyntaxNodeAnalysisContext context, InvocationExpressionSyntax invocation)
        {
            var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken);
            if (symbolInfo.Symbol is IMethodSymbol method)
            {
                var containingNs = method.ContainingType?.ContainingNamespace?.ToDisplayString();
                return containingNs == "Cysharp.Threading.Tasks";
            }
            return false;
        }
    }
}

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
    /// Detects miscellaneous UniTask patterns that need migration.
    /// Covers MIG002 (cancelImmediately), MIG005 (unbounded channel),
    /// MIG006 (PlayerLoopTiming namespace), MIG012 (Lazy/Defer), MIG014 (AsyncEnumerable).
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UniTaskMiscAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
            => ImmutableArray.Create(
                MigrationDescriptors.CancelImmediately,
                MigrationDescriptors.UnboundedChannel,
                MigrationDescriptors.PlayerLoopTimingNamespace,
                MigrationDescriptors.LazyDefer,
                MigrationDescriptors.AsyncEnumerable);

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

                compilationCtx.RegisterSyntaxNodeAction(AnalyzeArgument, SyntaxKind.Argument);
                compilationCtx.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
                compilationCtx.RegisterSyntaxNodeAction(AnalyzeTypeSyntax, SyntaxKind.IdentifierName, SyntaxKind.QualifiedName, SyntaxKind.GenericName);
            });
        }

        static void AnalyzeArgument(SyntaxNodeAnalysisContext context)
        {
            var argument = (ArgumentSyntax)context.Node;

            // Verify it's inside a method call
            var invocation = argument.Parent?.Parent as InvocationExpressionSyntax;
            if (invocation == null) return;

            var methodSymbol = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol as IMethodSymbol;
            if (methodSymbol == null) return;

            var containingNs = methodSymbol.ContainingType?.ContainingNamespace?.ToDisplayString();
            if (containingNs != "Cysharp.Threading.Tasks") return;

            // Check named argument
            if (argument.NameColon != null)
            {
                if (argument.NameColon.Name.Identifier.Text == "cancelImmediately")
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        MigrationDescriptors.CancelImmediately,
                        argument.GetLocation()));
                }
                return;
            }

            // Check positional argument: find the parameter index for this argument
            var argList = argument.Parent as ArgumentListSyntax;
            if (argList == null) return;

            int argIndex = argList.Arguments.IndexOf(argument);
            if (argIndex >= 0 && argIndex < methodSymbol.Parameters.Length)
            {
                if (methodSymbol.Parameters[argIndex].Name == "cancelImmediately")
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        MigrationDescriptors.CancelImmediately,
                        argument.GetLocation()));
                }
            }
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
                case "Lazy":
                case "Defer":
                    if (IsUniTaskStaticCall(context, invocation))
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            MigrationDescriptors.LazyDefer,
                            invocation.GetLocation()));
                    }
                    break;

                case "CreateSingleConsumerUnbounded":
                    if (IsChannelCall(context, invocation))
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            MigrationDescriptors.UnboundedChannel,
                            invocation.GetLocation()));
                    }
                    break;
            }
        }

        static void AnalyzeTypeSyntax(SyntaxNodeAnalysisContext context)
        {
            var node = context.Node;
            var typeInfo = context.SemanticModel.GetTypeInfo(node, context.CancellationToken);
            var type = typeInfo.Type;
            if (type == null) return;

            var ns = type.ContainingNamespace?.ToDisplayString();
            if (ns != "Cysharp.Threading.Tasks") return;

            if (type.Name == "PlayerLoopTiming")
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    MigrationDescriptors.PlayerLoopTimingNamespace,
                    node.GetLocation()));
            }
            else if (type.Name.Contains("AsyncEnumerable") || type.Name == "IUniTaskAsyncEnumerable")
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    MigrationDescriptors.AsyncEnumerable,
                    node.GetLocation()));
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

        static bool IsChannelCall(SyntaxNodeAnalysisContext context, InvocationExpressionSyntax invocation)
        {
            if (invocation.Expression is MemberAccessExpressionSyntax ma)
            {
                var symbolInfo = context.SemanticModel.GetSymbolInfo(ma.Expression, context.CancellationToken);
                if (symbolInfo.Symbol is INamedTypeSymbol type)
                    return type.ContainingNamespace?.ToDisplayString() == "Cysharp.Threading.Tasks";
            }
            return false;
        }
    }
}

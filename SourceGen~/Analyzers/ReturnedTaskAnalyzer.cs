// Copyright (c) 2025 Una Partida Mas. All rights reserved.
// Licensed under the Valkarn Tasks License. See LICENSE.md in the repository root.

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace UnaPartidaMas.Valkarn.Tasks.SourceGen.Analyzers
{
    /// <summary>
    /// Reports TT013 when a ValkarnTask value is produced but never consumed.
    ///
    /// Currently deferred: TT002 (UnawaitedTaskAnalyzer) already handles bare
    /// expression-statement invocations at Error severity. TT013 is reserved
    /// for future data-flow analysis detecting assigned-but-never-awaited
    /// ValkarnTask variables, which TT002 does not cover.
    ///
    /// Skips methods decorated with [FireAndForget].
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class ReturnedTaskAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
            => ImmutableArray.Create(DiagnosticDescriptors.ReturnedTaskNotConsumed);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();

            // TODO: When data-flow analysis is implemented, register on
            // LocalDeclarationStatement/AssignmentExpression to detect
            // ValkarnTask variables that are never awaited or discarded.
            // For now, no syntax actions are registered because the
            // expression-statement case is fully handled by TT002.
        }

        // ── Helpers preserved for future data-flow expansion ──

        internal static bool IsForgetChain(ExpressionSyntax expression)
        {
            if (expression is InvocationExpressionSyntax outerInvocation
                && outerInvocation.Expression is MemberAccessExpressionSyntax memberAccess
                && memberAccess.Name.Identifier.Text == "Forget")
            {
                return true;
            }
            return false;
        }

        internal static bool HasFireAndForgetAttribute(IMethodSymbol method)
        {
            foreach (var attr in method.GetAttributes())
            {
                var name = attr.AttributeClass?.Name;
                if (name == "FireAndForgetAttribute" || name == "FireAndForget")
                    return true;
            }
            return false;
        }

        internal static string GetMethodName(InvocationExpressionSyntax invocation)
        {
            if (invocation.Expression is MemberAccessExpressionSyntax ma)
                return ma.Name.Identifier.Text;
            if (invocation.Expression is IdentifierNameSyntax id)
                return id.Identifier.Text;
            return invocation.Expression.ToString();
        }
    }
}

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
    /// Reports TT002 when a ValkarnTask-returning method call is used as
    /// an expression statement (not awaited, not assigned, not passed as argument,
    /// and not followed by .Forget()).
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UnawaitedTaskAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
            => ImmutableArray.Create(DiagnosticDescriptors.UnawaitedTask);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxNodeAction(AnalyzeExpressionStatement, SyntaxKind.ExpressionStatement);
        }

        static void AnalyzeExpressionStatement(SyntaxNodeAnalysisContext context)
        {
            var statement = (ExpressionStatementSyntax)context.Node;
            var expression = statement.Expression;

            // Skip assignment expressions (e.g., tasks[i] = DoWork()) — value is consumed
            if (expression is AssignmentExpressionSyntax)
                return;

            // Skip: x.Forget() — intentional fire-and-forget
            if (expression is InvocationExpressionSyntax invocation
                && invocation.Expression is MemberAccessExpressionSyntax memberAccess
                && memberAccess.Name.Identifier.Text == "Forget")
            {
                return;
            }

            var typeInfo = context.SemanticModel.GetTypeInfo(expression, context.CancellationToken);
            if (ValkarnTypeHelper.IsAnyValkarnTask(typeInfo.Type))
            {
                var methodName = GetMethodName(expression);
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.UnawaitedTask,
                    expression.GetLocation(),
                    methodName));
            }
        }

        static string GetMethodName(ExpressionSyntax expr)
        {
            if (expr is InvocationExpressionSyntax inv)
            {
                if (inv.Expression is MemberAccessExpressionSyntax ma)
                    return ma.Name.Identifier.Text;
                if (inv.Expression is IdentifierNameSyntax id)
                    return id.Identifier.Text;
            }
            return expr.ToString();
        }
    }
}

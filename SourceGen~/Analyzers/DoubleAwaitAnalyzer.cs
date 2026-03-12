// Copyright (c) 2025 Una Partida Mas. All rights reserved.
// Licensed under the Valkarn Tasks License. See LICENSE.md in the repository root.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace UnaPartidaMas.Valkarn.Tasks.SourceGen.Analyzers
{
    /// <summary>
    /// Reports TT001 when the same ValkarnTask local variable is awaited
    /// more than once. ValkarnTask is single-consumption: the second await will
    /// encounter a stale token and throw.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class DoubleAwaitAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
            => ImmutableArray.Create(DiagnosticDescriptors.DoubleAwait);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
        }

        static void AnalyzeMethod(SyntaxNodeAnalysisContext context)
        {
            var method = (MethodDeclarationSyntax)context.Node;
            if (method.Body == null && method.ExpressionBody == null) return;

            var awaitedSymbols = new HashSet<ISymbol>(SymbolEqualityComparer.Default);

            foreach (var awaitExpr in method.DescendantNodes().OfType<AwaitExpressionSyntax>())
            {
                // Handle both simple identifiers and this.field member access
                ExpressionSyntax nameExpr = awaitExpr.Expression;
                if (nameExpr is MemberAccessExpressionSyntax memberAccess
                    && memberAccess.Expression is ThisExpressionSyntax)
                {
                    nameExpr = memberAccess.Name;
                }

                if (!(nameExpr is IdentifierNameSyntax identifier))
                    continue;

                var symbolInfo = context.SemanticModel.GetSymbolInfo(identifier, context.CancellationToken);
                var symbol = symbolInfo.Symbol;

                ITypeSymbol type = symbol switch
                {
                    ILocalSymbol local => local.Type,
                    IParameterSymbol param => param.Type,
                    IFieldSymbol field => field.Type,
                    _ => null
                };

                if (type == null || !ValkarnTypeHelper.IsAnyValkarnTask(type))
                    continue;

                if (!awaitedSymbols.Add(symbol))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        DiagnosticDescriptors.DoubleAwait,
                        awaitExpr.GetLocation()));
                }
            }
        }
    }
}

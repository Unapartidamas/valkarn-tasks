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
    /// Reports TT011 when WhenAll is called with tasks that have different lifetime
    /// scopes — e.g., one task is bound to a MonoBehaviour (auto-cancelled on Destroy)
    /// and another is unbound. This can cause surprising partial-cancellation behavior.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class MixedLifetimeAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
            => ImmutableArray.Create(DiagnosticDescriptors.MixedLifetimeWhenAll);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
        }

        static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
        {
            var invocation = (InvocationExpressionSyntax)context.Node;

            // Check if this is ValkarnTask.WhenAll(...)
            if (!IsWhenAllCall(invocation, context.SemanticModel, context.CancellationToken))
                return;

            var args = invocation.ArgumentList.Arguments;
            if (args.Count < 2)
                return;

            // Classify each argument as "bound" or "unbound"
            string firstBoundName = null;
            string firstUnboundName = null;

            foreach (var arg in args)
            {
                var argExpr = arg.Expression;
                var name = GetExpressionName(argExpr);
                var isBound = IsMonoBehaviourBoundTask(argExpr, context.SemanticModel, context.CancellationToken);

                if (isBound && firstBoundName == null)
                    firstBoundName = name;
                if (!isBound && firstUnboundName == null)
                    firstUnboundName = name;

                if (firstBoundName != null && firstUnboundName != null)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        DiagnosticDescriptors.MixedLifetimeWhenAll,
                        invocation.GetLocation(),
                        firstBoundName,
                        firstUnboundName));
                    return;
                }
            }
        }

        static bool IsWhenAllCall(InvocationExpressionSyntax invocation, SemanticModel model,
            System.Threading.CancellationToken ct)
        {
            var symbolInfo = model.GetSymbolInfo(invocation, ct);
            if (symbolInfo.Symbol is IMethodSymbol method)
            {
                return method.Name == "WhenAll"
                    && ValkarnTypeHelper.IsValkarnTask(method.ContainingType);
            }
            return false;
        }

        static bool IsMonoBehaviourBoundTask(ExpressionSyntax expression, SemanticModel model,
            System.Threading.CancellationToken ct)
        {
            // If the argument is a method invocation, check if the method is declared
            // in a class that extends MonoBehaviour
            if (expression is InvocationExpressionSyntax innerInvocation)
            {
                var symbolInfo = model.GetSymbolInfo(innerInvocation, ct);
                if (symbolInfo.Symbol is IMethodSymbol method)
                {
                    var containingType = method.ContainingType;
                    return IsMonoBehaviour(containingType);
                }
            }

            // If the argument is a member access (this.X() or obj.X()), check receiver
            if (expression is MemberAccessExpressionSyntax memberAccess)
            {
                var receiverType = model.GetTypeInfo(memberAccess.Expression, ct).Type;
                if (receiverType is INamedTypeSymbol named)
                    return IsMonoBehaviour(named);
            }

            return false;
        }

        static bool IsMonoBehaviour(INamedTypeSymbol type)
        {
            if (type == null) return false;
            var current = type.BaseType;
            while (current != null)
            {
                if (current.Name == "MonoBehaviour"
                    && current.ContainingNamespace?.ToDisplayString() == "UnityEngine")
                    return true;
                current = current.BaseType;
            }
            return false;
        }

        static string GetExpressionName(ExpressionSyntax expression)
        {
            if (expression is InvocationExpressionSyntax invocation)
            {
                if (invocation.Expression is MemberAccessExpressionSyntax ma)
                    return ma.Name.Identifier.Text + "()";
                if (invocation.Expression is IdentifierNameSyntax id)
                    return id.Identifier.Text + "()";
            }
            return expression.ToString();
        }
    }
}

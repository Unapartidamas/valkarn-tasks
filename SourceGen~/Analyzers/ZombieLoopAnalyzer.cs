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
    /// Reports TT012 when an async ValkarnTask method contains a loop (for, while,
    /// foreach, do-while) whose body has no cancellation check.
    /// A cancellation check is any of: ThrowIfCancellationRequested,
    /// IsCancellationRequested, or a CancellationToken parameter reference.
    /// Without a check, lifecycle cancellation cannot break the loop, creating
    /// a "zombie" loop that runs indefinitely.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class ZombieLoopAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
            => ImmutableArray.Create(DiagnosticDescriptors.ZombieLoop);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
        }

        static void AnalyzeMethod(SyntaxNodeAnalysisContext context)
        {
            var method = (MethodDeclarationSyntax)context.Node;

            if (!method.Modifiers.Any(SyntaxKind.AsyncKeyword))
                return;

            var returnType = context.SemanticModel.GetTypeInfo(method.ReturnType, context.CancellationToken).Type;
            if (!ValkarnTypeHelper.IsAnyValkarnTask(returnType))
                return;

            // Scan all loops that are direct descendants of this method
            // (not loops inside nested lambdas/local functions)
            foreach (var loop in method.DescendantNodes(n => !IsNestedFunctionLike(n)))
            {
                StatementSyntax body;
                SyntaxNode loopNode;

                switch (loop)
                {
                    case ForStatementSyntax forLoop:
                        body = forLoop.Statement;
                        loopNode = forLoop;
                        break;
                    case WhileStatementSyntax whileLoop:
                        body = whileLoop.Statement;
                        loopNode = whileLoop;
                        break;
                    case ForEachStatementSyntax foreachLoop:
                        body = foreachLoop.Statement;
                        loopNode = foreachLoop;
                        break;
                    case DoStatementSyntax doLoop:
                        body = doLoop.Statement;
                        loopNode = doLoop;
                        break;
                    default:
                        continue;
                }

                if (body == null)
                    continue;

                if (!HasCancellationCheck(body))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        DiagnosticDescriptors.ZombieLoop,
                        loopNode.GetLocation(),
                        method.Identifier.Text));
                }
            }
        }

        static bool HasCancellationCheck(StatementSyntax body)
        {
            foreach (var node in body.DescendantNodes())
            {
                // An await expression is a valid cancellation point — the awaited
                // operation can observe a CancellationToken and throw OCE.
                if (node is AwaitExpressionSyntax)
                    return true;

                if (node is IdentifierNameSyntax identifier)
                {
                    var name = identifier.Identifier.Text;
                    if (name == "ThrowIfCancellationRequested"
                        || name == "IsCancellationRequested")
                    {
                        return true;
                    }
                }

                // Also check if a CancellationToken parameter is referenced
                // (e.g., passing `ct` to another method, checking `ct.XYZ`, etc.)
                if (node is MemberAccessExpressionSyntax memberAccess)
                {
                    var memberName = memberAccess.Name.Identifier.Text;
                    if (memberName == "ThrowIfCancellationRequested"
                        || memberName == "IsCancellationRequested")
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        static bool IsNestedFunctionLike(SyntaxNode node)
        {
            return node is LocalFunctionStatementSyntax
                || node is AnonymousFunctionExpressionSyntax;
        }
    }
}

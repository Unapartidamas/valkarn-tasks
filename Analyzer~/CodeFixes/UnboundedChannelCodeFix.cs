// Copyright (c) 2025 Una Partida Mas. All rights reserved.
// Licensed under the Valkarn Tasks License. See LICENSE.md in the repository root.

using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace UnaPartidaMas.Valkarn.Tasks.Analyzer.CodeFixes
{
    /// <summary>
    /// Code fix for MIG005: Replaces Channel.CreateSingleConsumerUnbounded&lt;T&gt;()
    /// with ValkarnTask.Channel.CreateUnbounded&lt;T&gt;().
    /// </summary>
    [ExportCodeFixProvider(LanguageNames.CSharp), Shared]
    public sealed class UnboundedChannelCodeFix : CodeFixProvider
    {
        public override ImmutableArray<string> FixableDiagnosticIds
            => ImmutableArray.Create("MIG005");

        public override FixAllProvider GetFixAllProvider()
            => WellKnownFixAllProviders.BatchFixer;

        public override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken);
            var diagnostic = context.Diagnostics[0];
            var node = root.FindNode(diagnostic.Location.SourceSpan);

            if (node is InvocationExpressionSyntax)
            {
                context.RegisterCodeFix(
                    CodeAction.Create(
                        "Replace with ValkarnTask.Channel.CreateUnbounded<T>()",
                        ct => ReplaceChannelAsync(context.Document, node, ct),
                        "MIG005_Fix"),
                    diagnostic);
            }
        }

        static async Task<Document> ReplaceChannelAsync(Document document, SyntaxNode node, CancellationToken ct)
        {
            var root = await document.GetSyntaxRootAsync(ct);
            var invocation = (InvocationExpressionSyntax)node;

            // Extract the type argument from CreateSingleConsumerUnbounded<T>()
            string typeArg = "T";
            if (invocation.Expression is MemberAccessExpressionSyntax memberAccess
                && memberAccess.Name is GenericNameSyntax genericName
                && genericName.TypeArgumentList.Arguments.Count > 0)
            {
                typeArg = genericName.TypeArgumentList.Arguments[0].ToString();
            }

            var newExpr = SyntaxFactory.ParseExpression(
                $"ValkarnTask.Channel.CreateUnbounded<{typeArg}>()")
                .WithTriviaFrom(node);

            var newRoot = root.ReplaceNode(node, newExpr);
            return await UsingHelper.EnsureValkarnUsingAsync(document.WithSyntaxRoot(newRoot), ct);
        }
    }
}

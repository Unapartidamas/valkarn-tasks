// Copyright (c) 2025 Una Partida Mas. All rights reserved.
// Licensed under the Valkarn Tasks License. See LICENSE.md in the repository root.

using System.Collections.Immutable;
using System.Composition;
using System.Linq;
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
    /// Code fix for MIG015: Replaces new TimeoutController() with
    /// new CancellationTokenSource(TimeSpan).
    /// </summary>
    [ExportCodeFixProvider(LanguageNames.CSharp), Shared]
    public sealed class TimeoutControllerCodeFix : CodeFixProvider
    {
        public override ImmutableArray<string> FixableDiagnosticIds
            => ImmutableArray.Create("MIG015");

        public override FixAllProvider GetFixAllProvider()
            => WellKnownFixAllProviders.BatchFixer;

        public override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken);
            var diagnostic = context.Diagnostics[0];
            var node = root.FindNode(diagnostic.Location.SourceSpan);

            if (node is ObjectCreationExpressionSyntax)
            {
                context.RegisterCodeFix(
                    CodeAction.Create(
                        "Replace with CancellationTokenSource",
                        ct => ReplaceAsync(context.Document, node, ct),
                        "MIG015_Fix"),
                    diagnostic);
            }
        }

        static async Task<Document> ReplaceAsync(Document document, SyntaxNode node, CancellationToken ct)
        {
            var root = await document.GetSyntaxRootAsync(ct);
            var creation = (ObjectCreationExpressionSyntax)node;

            // new TimeoutController() → new CancellationTokenSource()
            // Preserve any arguments (though they'll need manual review)
            var newType = SyntaxFactory.ParseTypeName("CancellationTokenSource");
            var newCreation = creation.WithType(newType).WithTriviaFrom(node);

            var newRoot = root.ReplaceNode(node, newCreation);
            var newDoc = document.WithSyntaxRoot(newRoot);

            // Ensure using System.Threading is present (CancellationTokenSource lives there)
            newDoc = await EnsureUsingAsync(newDoc, "System.Threading", ct);
            return newDoc;
        }

        static async Task<Document> EnsureUsingAsync(Document document, string namespaceName, CancellationToken ct)
        {
            var root = await document.GetSyntaxRootAsync(ct);
            if (root is not CompilationUnitSyntax compilationUnit) return document;

            if (compilationUnit.Usings.Any(u => u.Name?.ToString() == namespaceName))
                return document;

            var newUsing = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(namespaceName))
                .NormalizeWhitespace()
                .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);
            var newRoot = compilationUnit.AddUsings(newUsing);
            return document.WithSyntaxRoot(newRoot);
        }
    }
}

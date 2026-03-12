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
    /// Code fix for MIG014: Replaces IUniTaskAsyncEnumerable&lt;T&gt; with
    /// IAsyncEnumerable&lt;T&gt; (System.Collections.Generic).
    /// </summary>
    [ExportCodeFixProvider(LanguageNames.CSharp), Shared]
    public sealed class AsyncEnumerableCodeFix : CodeFixProvider
    {
        public override ImmutableArray<string> FixableDiagnosticIds
            => ImmutableArray.Create("MIG014");

        public override FixAllProvider GetFixAllProvider()
            => WellKnownFixAllProviders.BatchFixer;

        public override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken);
            var diagnostic = context.Diagnostics[0];
            var node = root.FindNode(diagnostic.Location.SourceSpan);

            if (node is TypeSyntax)
            {
                context.RegisterCodeFix(
                    CodeAction.Create(
                        "Replace with IAsyncEnumerable<T>",
                        ct => ReplaceTypeAsync(context.Document, node, ct),
                        "MIG014_Fix"),
                    diagnostic);
            }
        }

        static async Task<Document> ReplaceTypeAsync(Document document, SyntaxNode node, CancellationToken ct)
        {
            var root = await document.GetSyntaxRootAsync(ct);
            var semanticModel = await document.GetSemanticModelAsync(ct);

            var typeInfo = semanticModel.GetTypeInfo((TypeSyntax)node, ct);
            SyntaxNode newNode;
            if (typeInfo.Type is INamedTypeSymbol { IsGenericType: true } named
                && named.TypeArguments.Length > 0)
            {
                var typeArg = named.TypeArguments[0].ToDisplayString();
                newNode = SyntaxFactory.ParseTypeName($"IAsyncEnumerable<{typeArg}>")
                    .WithTriviaFrom(node);
            }
            else
            {
                // Cannot determine type argument — do not apply fix
                return document;
            }

            var newRoot = root.ReplaceNode(node, newNode);
            var newDoc = document.WithSyntaxRoot(newRoot);

            // Ensure using System.Collections.Generic is present (IAsyncEnumerable lives there)
            newDoc = await EnsureUsingAsync(newDoc, "System.Collections.Generic", ct);
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

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
    /// Reports TT016 when an async method returning ValkarnTask or ValkarnTask{T}
    /// contains no await expressions. Suggests making it synchronous or using
    /// ValkarnTask.CompletedTask / ValkarnTask.FromResult().
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class AsyncWithoutAwaitAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
            => ImmutableArray.Create(DiagnosticDescriptors.AsyncWithoutAwait);

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

            // Check for await expressions, await foreach, and await using
            bool hasAwait = method.DescendantNodes().Any(n =>
                n is AwaitExpressionSyntax
                || (n is ForEachStatementSyntax fe && fe.AwaitKeyword != default)
                || (n is CommonForEachStatementSyntax cfe && cfe.AwaitKeyword != default)
                || (n is LocalDeclarationStatementSyntax ld && ld.AwaitKeyword != default)
                || (n is UsingStatementSyntax us && us.AwaitKeyword != default));
            if (!hasAwait)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.AsyncWithoutAwait,
                    method.Identifier.GetLocation(),
                    method.Identifier.Text));
            }
        }
    }
}

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
    /// Reports TT017 when [FireAndForget] is applied to a method returning
    /// ValkarnTask{T}. Fire-and-forget discards the return value, so the method
    /// should return ValkarnTask (void) instead.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class FireAndForgetOnTypedAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
            => ImmutableArray.Create(DiagnosticDescriptors.FireAndForgetOnTyped);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
        }

        static void AnalyzeMethod(SyntaxNodeAnalysisContext context)
        {
            var method = (MethodDeclarationSyntax)context.Node;

            bool hasFireAndForget = false;
            foreach (var attrList in method.AttributeLists)
            {
                foreach (var attr in attrList.Attributes)
                {
                    var name = attr.Name.ToString();
                    if (name == "FireAndForget" || name == "FireAndForgetAttribute")
                    {
                        hasFireAndForget = true;
                        break;
                    }
                }
                if (hasFireAndForget) break;
            }

            if (!hasFireAndForget) return;

            var returnType = context.SemanticModel.GetTypeInfo(method.ReturnType, context.CancellationToken).Type;
            if (ValkarnTypeHelper.IsValkarnTaskT(returnType))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.FireAndForgetOnTyped,
                    method.Identifier.GetLocation(),
                    method.Identifier.Text));
            }
        }
    }
}

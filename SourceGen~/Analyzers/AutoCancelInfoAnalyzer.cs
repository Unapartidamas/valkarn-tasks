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
    /// Reports TT010 (info) when an async ValkarnTask method in a MonoBehaviour
    /// will be auto-cancelled on Destroy. The developer can add [NoAutoCancel]
    /// to opt out.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class AutoCancelInfoAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
            => ImmutableArray.Create(DiagnosticDescriptors.AutoCancelActive);

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

            // Must be inside a MonoBehaviour class
            var containingClass = method.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault();
            if (containingClass == null)
                return;

            var classSymbol = context.SemanticModel.GetDeclaredSymbol(containingClass, context.CancellationToken);
            if (classSymbol == null || !IsMonoBehaviour(classSymbol))
                return;

            // Skip if [NoAutoCancel] is applied
            if (HasNoAutoCancelAttribute(method))
                return;

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.AutoCancelActive,
                method.Identifier.GetLocation(),
                method.Identifier.Text,
                containingClass.Identifier.Text));
        }

        static bool IsMonoBehaviour(INamedTypeSymbol classSymbol)
        {
            var current = classSymbol.BaseType;
            while (current != null)
            {
                if (current.Name == "MonoBehaviour"
                    && current.ContainingNamespace?.ToDisplayString() == "UnityEngine")
                    return true;
                current = current.BaseType;
            }
            return false;
        }

        static bool HasNoAutoCancelAttribute(MethodDeclarationSyntax method)
        {
            foreach (var attrList in method.AttributeLists)
            {
                foreach (var attr in attrList.Attributes)
                {
                    var name = attr.Name.ToString();
                    if (name == "NoAutoCancel" || name == "NoAutoCancelAttribute")
                        return true;
                }
            }
            return false;
        }
    }
}

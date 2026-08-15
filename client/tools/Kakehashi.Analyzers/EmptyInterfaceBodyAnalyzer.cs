using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Kakehashi.Analyzers;

/// <summary>
/// An interface with no members is written <c>interface IFoo;</c>, not <c>interface IFoo { }</c>.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class EmptyInterfaceBodyAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(StyleRules.EmptyInterfaceBody);

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.InterfaceDeclaration);
    }

    private static void Analyze(SyntaxNodeAnalysisContext context)
    {
        var declaration = (InterfaceDeclarationSyntax)context.Node;

        // A base list or a type-parameter constraint is not a member: an interface that only
        // names what it extends still declares nothing of its own.
        if (declaration.Members.Count > 0
            || declaration.OpenBraceToken.IsKind(SyntaxKind.None))
        {
            return;
        }

        // A comment between the braces has nowhere to go once they are gone.
        foreach (var trivia in declaration.CloseBraceToken.LeadingTrivia)
        {
            if (!trivia.IsKind(SyntaxKind.WhitespaceTrivia)
                && !trivia.IsKind(SyntaxKind.EndOfLineTrivia))
            {
                return;
            }
        }

        context.ReportDiagnostic(Diagnostic.Create(
            StyleRules.EmptyInterfaceBody,
            declaration.Identifier.GetLocation(),
            declaration.Identifier.ValueText));
    }
}

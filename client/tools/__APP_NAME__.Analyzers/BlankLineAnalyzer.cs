using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace __ROOT_NAMESPACE__.Analyzers;

/// <summary>
/// The four rules about a blank line: before <c>return</c>, before <c>if</c>, and on both sides of
/// the file-scoped namespace declaration.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class BlankLineAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(
            StyleRules.BlankLineBeforeReturn,
            StyleRules.BlankLineBeforeIf,
            StyleRules.BlankLineBeforeNamespace,
            StyleRules.BlankLineAfterNamespace);

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeReturn, SyntaxKind.ReturnStatement);
        context.RegisterSyntaxNodeAction(AnalyzeIf, SyntaxKind.IfStatement);
        context.RegisterSyntaxNodeAction(AnalyzeNamespace, SyntaxKind.FileScopedNamespaceDeclaration);
    }

    /// <summary>
    /// The statement before this one in the same list, or null when this one opens the block.
    /// </summary>
    /// <remarks>
    /// A switch section holds its statements directly rather than in a block, so both shapes are
    /// read here; anything else — an else clause, an unbraced body — is not a list and is skipped.
    /// </remarks>
    private static StatementSyntax? PrecedingStatement(StatementSyntax statement)
    {
        var siblings = statement.Parent switch {
            BlockSyntax block => block.Statements,
            SwitchSectionSyntax section => section.Statements,
            _ => default,
        };

        if (siblings.Count == 0)
        {
            return null;
        }

        var index = siblings.IndexOf(statement);

        return index > 0 ? siblings[index - 1] : null;
    }

    private static void AnalyzeReturn(SyntaxNodeAnalysisContext context)
    {
        var statement = (ReturnStatementSyntax)context.Node;
        var previous = PrecedingStatement(statement);

        if (previous is null || Layout.IsSeparatedFrom(statement, previous))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            StyleRules.BlankLineBeforeReturn, statement.ReturnKeyword.GetLocation()));
    }

    private static void AnalyzeIf(SyntaxNodeAnalysisContext context)
    {
        var statement = (IfStatementSyntax)context.Node;

        // An `else if` is one branch of the statement above it, not a new one, so it takes no
        // blank line: its parent is the else clause rather than a statement list.
        var previous = PrecedingStatement(statement);

        if (previous is null || Layout.IsSeparatedFrom(statement, previous))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            StyleRules.BlankLineBeforeIf, statement.IfKeyword.GetLocation()));
    }

    private static void AnalyzeNamespace(SyntaxNodeAnalysisContext context)
    {
        var declaration = (FileScopedNamespaceDeclarationSyntax)context.Node;

        AnalyzeNamespaceAbove(context, declaration);
        AnalyzeNamespaceBelow(context, declaration);
    }

    private static void AnalyzeNamespaceAbove(
        SyntaxNodeAnalysisContext context, FileScopedNamespaceDeclarationSyntax declaration)
    {
        if (declaration.Parent is not CompilationUnitSyntax unit)
        {
            return;
        }

        // Nothing above it in a file with no usings, and nothing to separate it from.
        SyntaxNode? previous = unit.Usings.Count > 0
            ? unit.Usings[unit.Usings.Count - 1]
            : unit.Externs.Count > 0 ? unit.Externs[unit.Externs.Count - 1] : null;

        if (previous is null || Layout.IsSeparatedFrom(declaration, previous))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            StyleRules.BlankLineBeforeNamespace, declaration.NamespaceKeyword.GetLocation()));
    }

    private static void AnalyzeNamespaceBelow(
        SyntaxNodeAnalysisContext context, FileScopedNamespaceDeclarationSyntax declaration)
    {
        SyntaxNode? first = declaration.Members.Count > 0 ? declaration.Members[0] : null;

        if (first is null)
        {
            return;
        }

        var semicolonLine = declaration.SemicolonToken.GetLocation().GetLineSpan()
            .EndLinePosition.Line;

        if (Layout.AttachedStartLine(first) - semicolonLine >= 2)
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            StyleRules.BlankLineAfterNamespace, declaration.SemicolonToken.GetLocation()));
    }
}

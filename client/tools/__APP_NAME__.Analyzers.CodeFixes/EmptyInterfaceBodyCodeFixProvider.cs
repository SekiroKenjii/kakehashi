using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace __ROOT_NAMESPACE__.Analyzers.CodeFixes;

/// <summary>
/// Replaces an empty interface body with a semicolon (KH0005).
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(EmptyInterfaceBodyCodeFixProvider))]
[Shared]
public sealed class EmptyInterfaceBodyCodeFixProvider : CodeFixProvider
{
    private const string _title = "End the interface with ';'";

    /// <inheritdoc/>
    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        ImmutableArray.Create("KH0005");

    /// <inheritdoc/>
    public override FixAllProvider GetFixAllProvider()
    {
        return WellKnownFixAllProviders.BatchFixer;
    }

    /// <inheritdoc/>
    public override Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        foreach (var diagnostic in context.Diagnostics)
        {
            context.RegisterCodeFix(
                CodeAction.Create(
                    _title,
                    token => ReplaceBodyAsync(context.Document, diagnostic, token),
                    equivalenceKey: _title),
                diagnostic);
        }

        return Task.CompletedTask;
    }

    private static async Task<Document> ReplaceBodyAsync(
        Document document, Diagnostic diagnostic, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);

        if (root is null)
        {
            return document;
        }

        var declaration = root.FindToken(diagnostic.Location.SourceSpan.Start).Parent?
            .FirstAncestorOrSelf<InterfaceDeclarationSyntax>();

        if (declaration is null)
        {
            return document;
        }

        var semicolon = SyntaxFactory.Token(SyntaxKind.SemicolonToken)
            .WithTrailingTrivia(declaration.CloseBraceToken.TrailingTrivia);

        var updated = declaration
            .WithOpenBraceToken(default)
            .WithCloseBraceToken(default)
            .WithSemicolonToken(semicolon);

        // Whatever preceded the brace kept the space that separated them; the semicolon is
        // written tight against the name, the base list, or the last constraint.
        var previous = updated.SemicolonToken.GetPreviousToken();
        updated = updated.ReplaceToken(
            previous, previous.WithTrailingTrivia(SyntaxTriviaList.Empty));

        return document.WithSyntaxRoot(root.ReplaceNode(declaration, updated));
    }
}

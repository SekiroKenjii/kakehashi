using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;

namespace Kakehashi.Analyzers.CodeFixes;

/// <summary>
/// Inserts the missing blank line for KH0001 through KH0004.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(BlankLineCodeFixProvider))]
[Shared]
public sealed class BlankLineCodeFixProvider : CodeFixProvider
{
    private const string _title = "Insert a blank line";

    /// <inheritdoc/>
    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        ImmutableArray.Create("KH0001", "KH0002", "KH0003", "KH0004");

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
                    token => InsertBlankLineAsync(context.Document, diagnostic, token),
                    equivalenceKey: _title),
                diagnostic);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// The token the blank line goes above: the top of the comment block attached to the reported
    /// construct, or the construct itself when nothing is attached.
    /// </summary>
    /// <remarks>
    /// KH0004 reports on the namespace's semicolon rather than on what follows it, so that case
    /// steps forward to the next token before looking for a comment block.
    /// </remarks>
    private static SyntaxToken TargetToken(SyntaxNode root, Diagnostic diagnostic)
    {
        var token = root.FindToken(diagnostic.Location.SourceSpan.Start);

        if (diagnostic.Id == "KH0004")
        {
            token = token.GetNextToken();
        }

        return token;
    }

    private static async Task<Document> InsertBlankLineAsync(
        Document document, Diagnostic diagnostic, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);

        if (root is null)
        {
            return document;
        }

        var token = TargetToken(root, diagnostic);
        var leading = token.LeadingTrivia;

        // Ahead of the comment block, not between the comment and its subject: everything from
        // the first attached comment onwards moves down together.
        var insertAt = leading.Count;

        for (var i = leading.Count - 1; i >= 0; i--)
        {
            var trivia = leading[i];

            if (trivia.IsKind(SyntaxKind.WhitespaceTrivia)
                || trivia.IsKind(SyntaxKind.EndOfLineTrivia))
            {
                continue;
            }

            if (IsComment(trivia))
            {
                insertAt = i;

                continue;
            }

            break;
        }

        // Back past the indentation of the line the comment block opens on, so the inserted line
        // is empty rather than a line of spaces.
        if (insertAt == leading.Count)
        {
            insertAt = 0;
        }

        while (insertAt > 0 && leading[insertAt - 1].IsKind(SyntaxKind.WhitespaceTrivia))
        {
            insertAt--;
        }

        var updated = leading.Insert(insertAt, SyntaxFactory.LineFeed);

        return document.WithSyntaxRoot(
            root.ReplaceToken(token, token.WithLeadingTrivia(updated)));
    }

    private static bool IsComment(SyntaxTrivia trivia)
    {
        return trivia.IsKind(SyntaxKind.SingleLineCommentTrivia)
            || trivia.IsKind(SyntaxKind.MultiLineCommentTrivia)
            || trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia)
            || trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia);
    }
}

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Kakehashi.Analyzers;

/// <summary>
/// Line arithmetic shared by the blank-line rules.
/// </summary>
internal static class Layout
{
    /// <summary>
    /// Whether a blank line separates <paramref name="node"/> from what precedes it.
    /// </summary>
    /// <remarks>
    /// The measurement starts at the comment block attached to the node, not at the node, so the
    /// blank line is required above the comment rather than between the comment and its subject.
    /// </remarks>
    internal static bool IsSeparatedFrom(SyntaxNode node, SyntaxNode previous)
    {
        var previousEnd = previous.GetLocation().GetLineSpan().EndLinePosition.Line;

        return AttachedStartLine(node) - previousEnd >= 2;
    }

    /// <summary>
    /// The line the node begins on, counting a comment block written directly above it as part
    /// of the node.
    /// </summary>
    /// <remarks>
    /// A comment that is itself separated from the node by a blank line belongs to whatever came
    /// before, so the walk stops there.
    /// </remarks>
    internal static int AttachedStartLine(SyntaxNode node)
    {
        var token = node.GetFirstToken();
        var leading = token.LeadingTrivia;
        var start = node.GetLocation().GetLineSpan().StartLinePosition.Line;
        var consecutiveEndOfLines = 0;

        for (var i = leading.Count - 1; i >= 0; i--)
        {
            var trivia = leading[i];

            if (trivia.IsKind(SyntaxKind.WhitespaceTrivia))
            {
                continue;
            }

            if (trivia.IsKind(SyntaxKind.EndOfLineTrivia))
            {
                consecutiveEndOfLines++;

                if (consecutiveEndOfLines >= 2)
                {
                    break;
                }

                continue;
            }

            if (IsComment(trivia))
            {
                consecutiveEndOfLines = 0;
                start = trivia.GetLocation().GetLineSpan().StartLinePosition.Line;

                continue;
            }

            break;
        }

        return start;
    }

    private static bool IsComment(SyntaxTrivia trivia)
    {
        return trivia.IsKind(SyntaxKind.SingleLineCommentTrivia)
            || trivia.IsKind(SyntaxKind.MultiLineCommentTrivia)
            || trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia)
            || trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia);
    }
}

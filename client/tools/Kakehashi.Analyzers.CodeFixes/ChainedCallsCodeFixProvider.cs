using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;

namespace Kakehashi.Analyzers.CodeFixes;

/// <summary>
/// Breaks a chain of calls onto one line each (KH0006).
/// </summary>
/// <remarks>
/// Only the line breaks are inserted here; the indentation of the new lines is left to the
/// formatter, which is the thing that knows what a continuation line is indented by.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ChainedCallsCodeFixProvider))]
[Shared]
public sealed class ChainedCallsCodeFixProvider : CodeFixProvider
{
    private const string _title = "Put each call on its own line";

    /// <inheritdoc/>
    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        ImmutableArray.Create("KH0006");

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
                    token => BreakChainAsync(context.Document, diagnostic, token),
                    equivalenceKey: _title),
                diagnostic);
        }

        return Task.CompletedTask;
    }

    /// <summary>The outermost invocation of the chain the reported dot belongs to.</summary>
    private static InvocationExpressionSyntax? OutermostCall(SyntaxNode root, Diagnostic diagnostic)
    {
        var node = root.FindToken(diagnostic.Location.SourceSpan.Start).Parent?
            .FirstAncestorOrSelf<InvocationExpressionSyntax>();

        while (node is not null && CallChain.IsInnerLink(node))
        {
            node = (InvocationExpressionSyntax)node.Parent!.Parent!;
        }

        return node;
    }

    private static async Task<Document> BreakChainAsync(
        Document document, Diagnostic diagnostic, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);

        if (root is null)
        {
            return document;
        }

        var chain = OutermostCall(root, diagnostic);

        if (chain is null)
        {
            return document;
        }

        var dots = new List<SyntaxToken>();

        foreach (var link in CallChain.Read(chain))
        {
            if (!link.OnOwnLine)
            {
                dots.Add(link.Dot);
            }
        }

        if (dots.Count == 0)
        {
            return document;
        }

        var updated = chain
            .ReplaceTokens(
                dots,
                (original, rewritten) => rewritten.WithLeadingTrivia(
                    rewritten.LeadingTrivia.Insert(0, SyntaxFactory.LineFeed)))
            .WithAdditionalAnnotations(Formatter.Annotation);

        return document.WithSyntaxRoot(root.ReplaceNode(chain, updated));
    }
}

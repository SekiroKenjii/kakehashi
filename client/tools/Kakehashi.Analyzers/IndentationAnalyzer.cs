using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Kakehashi.Analyzers;

/// <summary>
/// Every line is indented by whole units of four spaces.
/// </summary>
/// <remarks>
/// This is the one rule here that is about a line rather than about syntax, and it exists because
/// of a gap: the formatter re-indents the lines it breaks and leaves every other line where it
/// found it. A continuation line inside an expression is therefore free to sit at any column, and
/// `dotnet format --verify-no-changes` will call the file clean.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class IndentationAnalyzer : DiagnosticAnalyzer
{
    private const int _unit = 4;

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(StyleRules.IndentationOffGrid);

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxTreeAction(Analyze);
    }

    private static void Analyze(SyntaxTreeAnalysisContext context)
    {
        var text = context.Tree.GetText(context.CancellationToken);
        var root = context.Tree.GetRoot(context.CancellationToken);

        foreach (var line in text.Lines)
        {
            var span = line.Span;

            if (span.Length == 0)
            {
                continue;
            }

            var indent = 0;

            while (indent < span.Length && text[span.Start + indent] == ' ')
            {
                indent++;
            }

            // A blank line carries no indentation to judge, and a line of nothing but whitespace
            // is what trim_trailing_whitespace is for.
            if (indent == span.Length || indent % _unit == 0)
            {
                continue;
            }

            // A line that falls inside a raw string, a verbatim string or a block comment is part
            // of one token, and its leading spaces are that token's value rather than layout.
            var token = root.FindToken(span.Start, findInsideTrivia: true);

            if (token.FullSpan.Start < span.Start && token.FullSpan.End > span.Start
                && !token.IsKind(SyntaxKind.EndOfLineTrivia))
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                StyleRules.IndentationOffGrid,
                Location.Create(context.Tree, new TextSpan(span.Start, indent)),
                indent));
        }
    }
}

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Kakehashi.Analyzers;

/// <summary>
/// Two or more calls chained together are written one per line, each line starting with the dot.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ChainedCallsAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(StyleRules.ChainedCallsOnePerLine);

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.InvocationExpression);
    }

    private static void Analyze(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        if (CallChain.IsInnerLink(invocation))
        {
            return;
        }

        var links = CallChain.Read(invocation);

        if (links.Count < 2)
        {
            return;
        }

        foreach (var link in links)
        {
            if (link.OnOwnLine)
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                StyleRules.ChainedCallsOnePerLine, link.Dot.GetLocation(), links.Count));

            return;
        }
    }
}

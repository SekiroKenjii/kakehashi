using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kakehashi.Analyzers;

/// <summary>One call in a chain: the dot that introduces it and the name it invokes.</summary>
public readonly struct ChainLink
{
    /// <summary>Creates a link.</summary>
    /// <param name="dot">The <c>.</c> that introduces the call.</param>
    /// <param name="onOwnLine">Whether the dot is the first thing on its line.</param>
    public ChainLink(SyntaxToken dot, bool onOwnLine)
    {
        Dot = dot;
        OnOwnLine = onOwnLine;
    }

    /// <summary>The <c>.</c> that introduces the call.</summary>
    public SyntaxToken Dot { get; }

    /// <summary>Whether the dot already starts its own line.</summary>
    public bool OnOwnLine { get; }
}

/// <summary>
/// Reads a chain of calls off an invocation.
/// </summary>
public static class CallChain
{
    /// <summary>
    /// Names that ride on the call before them rather than forming a step of their own.
    /// </summary>
    /// <remarks>
    /// <c>ConfigureAwait</c> is ceremony for the awaiter, and the NSubstitute verbs are the
    /// predicate of an arrange or assert, not a stage of a pipeline. Breaking either one onto its
    /// own line separates a call from the thing that qualifies it.
    /// </remarks>
    public static readonly HashSet<string> Passengers = new(System.StringComparer.Ordinal)
    {
        "ConfigureAwait",
        "Returns",
        "ReturnsForAnyArgs",
        "ReturnsNull",
        "ReturnsNullForAnyArgs",
        "Throws",
        "ThrowsAsync",
        "ThrowsForAnyArgs",
        "Received",
        "ReceivedWithAnyArgs",
        "DidNotReceive",
        "DidNotReceiveWithAnyArgs",
    };

    /// <summary>
    /// Whether this invocation is an inner link of a longer chain, in which case the outermost
    /// call speaks for the whole of it.
    /// </summary>
    public static bool IsInnerLink(InvocationExpressionSyntax invocation)
    {
        return invocation.Parent is MemberAccessExpressionSyntax parent
            && parent.Expression == invocation
            && parent.Parent is InvocationExpressionSyntax;
    }

    /// <summary>
    /// The links of the chain ending at <paramref name="invocation"/>, outermost first, with the
    /// passengers left out.
    /// </summary>
    public static List<ChainLink> Read(InvocationExpressionSyntax invocation)
    {
        var links = new List<ChainLink>();
        var current = invocation;

        while (current.Expression is MemberAccessExpressionSyntax access)
        {
            var dot = access.OperatorToken;

            if (!Passengers.Contains(access.Name.Identifier.ValueText))
            {
                var dotLine = dot.GetLocation().GetLineSpan().StartLinePosition.Line;
                var beforeLine = dot.GetPreviousToken().GetLocation().GetLineSpan()
                    .EndLinePosition.Line;

                links.Add(new ChainLink(dot, beforeLine != dotLine));
            }

            if (access.Expression is not InvocationExpressionSyntax inner)
            {
                break;
            }

            current = inner;
        }

        return links;
    }
}

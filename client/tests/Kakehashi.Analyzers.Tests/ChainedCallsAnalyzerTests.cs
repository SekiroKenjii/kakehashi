using System.Threading.Tasks;
using Xunit;

namespace Kakehashi.Analyzers.Tests;

public sealed class ChainedCallsAnalyzerTests
{
    private static ChainedCallsAnalyzer CreateAnalyzer()
    {
        return new ChainedCallsAnalyzer();
    }

    private static string Wrap(string body)
    {
        return $$"""
            using System;
            using System.Collections.Generic;
            using System.Linq;

            namespace N;

            class C
            {
                void M(List<string> items, string s)
                {
            {{body}}
                }
            }
            """;
    }

    [Fact]
    public async Task TwoCallsOnOneLine_AreReported()
    {
        var lines = await Harness.LinesAsync(
            CreateAnalyzer(), Wrap("        var r = items.Where(x => x != null).ToList();"));

        Assert.Equal([11], lines);
    }

    [Fact]
    public async Task AChainAlreadyBrokenIsNotReported()
    {
        const string body = """
                    var r = items
                        .Where(x => x != null)
                        .Select(x => x.Length)
                        .ToList();
            """;

        Assert.Empty(await Harness.LinesAsync(CreateAnalyzer(), Wrap(body)));
    }

    [Fact]
    public async Task AChainWithOneGluedDotIsReported()
    {
        const string body = """
                    var r = items
                        .Where(x => x != null).Select(x => x.Length)
                        .ToList();
            """;

        var lines = await Harness.LinesAsync(CreateAnalyzer(), Wrap(body));

        Assert.Equal([12], lines);
    }

    [Fact]
    public async Task ASingleCallIsNotAChain()
    {
        Assert.Empty(await Harness.LinesAsync(
            CreateAnalyzer(), Wrap("        var r = items.ToList();")));
    }

    [Fact]
    public async Task ACallFollowedByAPropertyReadIsNotASecondCall()
    {
        Assert.Empty(await Harness.LinesAsync(
            CreateAnalyzer(), Wrap("        var r = items.ToList().Count;")));
    }

    [Fact]
    public async Task ConfigureAwaitRidesOnTheCallBeforeIt()
    {
        const string body = """
                    var t = System.Threading.Tasks.Task.FromResult(1).ConfigureAwait(false);
            """;

        Assert.Empty(await Harness.LinesAsync(CreateAnalyzer(), Wrap(body)));
    }

    [Fact]
    public async Task TheSubstituteVerbsRideOnTheCallBeforeThem()
    {
        const string body = """
                    s.Trim().Returns("x");
                    s.Trim().Received(1);
                    s.Trim().DidNotReceive();
            """;

        Assert.Empty(await Harness.LinesAsync(CreateAnalyzer(), Wrap(body)));
    }

    [Fact]
    public async Task APassengerDoesNotHideARealChainBehindIt()
    {
        const string body = """
                    var r = items.Where(x => x != null).ToList().Returns();
            """;

        var lines = await Harness.LinesAsync(CreateAnalyzer(), Wrap(body));

        Assert.Equal([11], lines);
    }

    [Fact]
    public async Task AChainRootedOnANewExpressionCountsOnlyItsCalls()
    {
        Assert.Empty(await Harness.LinesAsync(
            CreateAnalyzer(), Wrap("        var r = new List<string>().ToArray();")));
    }

    [Fact]
    public async Task OnlyTheOutermostCallOfAChainReportsOnce()
    {
        const string body = """
                    var r = items.Where(x => x != null).Select(x => x).ToList();
            """;

        Assert.Single(await Harness.ReportAsync(CreateAnalyzer(), Wrap(body)));
    }
}

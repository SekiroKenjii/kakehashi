using System.Threading.Tasks;
using Xunit;

namespace Kakehashi.Analyzers.Tests;

public sealed class BlankLineAnalyzerTests
{
    private static BlankLineAnalyzer CreateAnalyzer()
    {
        return new BlankLineAnalyzer();
    }

    [Fact]
    public async Task Return_AfterAStatement_IsReported()
    {
        const string source = """
            namespace N;

            class C
            {
                int M()
                {
                    var x = 1;
                    return x;
                }
            }
            """;

        var lines = await Harness.LinesAsync(CreateAnalyzer(), source);

        Assert.Equal([8], lines);
    }

    [Fact]
    public async Task Return_OpeningItsBlock_IsNotReported()
    {
        const string source = """
            namespace N;

            class C
            {
                int M()
                {
                    return 1;
                }

                int O(bool b)
                {
                    if (b)
                    {
                        return 2;
                    }

                    return 3;
                }
            }
            """;

        Assert.Empty(await Harness.LinesAsync(CreateAnalyzer(), source));
    }

    [Fact]
    public async Task Return_WithAnAttachedComment_WantsTheBlankLineAboveTheComment()
    {
        const string source = """
            namespace N;

            class C
            {
                int M()
                {
                    var x = 1;
                    // why
                    return x;
                }

                int O()
                {
                    var x = 1;

                    // why
                    return x;
                }
            }
            """;

        var lines = await Harness.LinesAsync(CreateAnalyzer(), source);

        Assert.Equal([9], lines);
    }

    [Fact]
    public async Task Return_FirstInASwitchSection_IsNotReported()
    {
        const string source = """
            namespace N;

            class C
            {
                int M(int i)
                {
                    switch (i)
                    {
                        case 1:
                            return 1;
                        default:
                            var x = 2;
                            return x;
                    }
                }
            }
            """;

        var lines = await Harness.LinesAsync(CreateAnalyzer(), source);

        Assert.Equal([13], lines);
    }

    [Fact]
    public async Task Return_InAnExpressionBodiedMember_IsNotAStatement()
    {
        const string source = """
            namespace N;

            class C
            {
                int M() => 1;

                int P => 2;
            }
            """;

        Assert.Empty(await Harness.LinesAsync(CreateAnalyzer(), source));
    }

    [Fact]
    public async Task If_AfterAStatement_IsReported()
    {
        const string source = """
            namespace N;

            class C
            {
                void M(bool b)
                {
                    var x = 1;
                    if (b)
                    {
                        x = 2;
                    }
                }
            }
            """;

        var lines = await Harness.LinesAsync(CreateAnalyzer(), source);

        Assert.Equal([8], lines);
    }

    [Fact]
    public async Task If_AsAnElseIf_IsNotReported()
    {
        const string source = """
            namespace N;

            class C
            {
                void M(int i)
                {
                    if (i == 1)
                    {
                        i = 2;
                    }
                    else if (i == 2)
                    {
                        i = 3;
                    }
                }
            }
            """;

        Assert.Empty(await Harness.LinesAsync(CreateAnalyzer(), source));
    }

    [Fact]
    public async Task If_AfterAnotherIfsClosingBrace_IsReported()
    {
        const string source = """
            namespace N;

            class C
            {
                void M(int i)
                {
                    if (i == 1)
                    {
                        i = 2;
                    }
                    if (i == 3)
                    {
                        i = 4;
                    }
                }
            }
            """;

        var lines = await Harness.LinesAsync(CreateAnalyzer(), source);

        Assert.Equal([11], lines);
    }

    [Fact]
    public async Task If_InsideALambdaBody_FollowsTheOrdinaryRule()
    {
        const string source = """
            using System;

            namespace N;

            class C
            {
                void M()
                {
                    Action<int> a = i =>
                    {
                        var x = i;
                        if (x > 0)
                        {
                            x = 0;
                        }
                    };
                }
            }
            """;

        var lines = await Harness.LinesAsync(CreateAnalyzer(), source);

        Assert.Equal([12], lines);
    }

    [Fact]
    public async Task If_InAStringOrACommentOrADirective_IsNotAStatement()
    {
        const string source = """
            namespace N;

            class C
            {
                void M()
                {
                    // if this fires the rule is textual
                    var s = "if (x) { }";
                    var t = s;
            #if DEBUG
                    t = s;
            #endif
                }
            }
            """;

        Assert.Empty(await Harness.LinesAsync(CreateAnalyzer(), source));
    }

    [Fact]
    public async Task Namespace_WithoutABlankLineEitherSide_IsReportedTwice()
    {
        const string source = """
            using System;
            namespace N;
            class C
            {
            }
            """;

        var lines = await Harness.LinesAsync(CreateAnalyzer(), source);

        Assert.Equal([2, 2], lines);
    }

    [Fact]
    public async Task Namespace_WithBlankLinesEitherSide_IsNotReported()
    {
        const string source = """
            using System;

            namespace N;

            class C
            {
            }
            """;

        Assert.Empty(await Harness.LinesAsync(CreateAnalyzer(), source));
    }

    [Fact]
    public async Task Namespace_WithNoUsingsAbove_HasNothingToSeparateFrom()
    {
        const string source = """
            namespace N;

            class C
            {
            }
            """;

        Assert.Empty(await Harness.LinesAsync(CreateAnalyzer(), source));
    }

    [Fact]
    public async Task Namespace_WithADocCommentOnTheFirstType_MeasuresFromTheComment()
    {
        const string source = """
            using System;

            namespace N;
            /// <summary>A.</summary>
            class C
            {
            }
            """;

        var lines = await Harness.LinesAsync(CreateAnalyzer(), source);

        Assert.Equal([3], lines);
    }
}

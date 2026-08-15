using System.Threading.Tasks;
using Xunit;

namespace __ROOT_NAMESPACE__.Analyzers.Tests;

public sealed class IndentationAnalyzerTests
{
    private static IndentationAnalyzer CreateAnalyzer()
    {
        return new IndentationAnalyzer();
    }

    [Fact]
    public async Task AContinuationLeftAtTheOldIndentIsReported()
    {
        const string source = """
            namespace N;

            class C
            {
                public static int[] Values { get; } = [
                  1,
              2,
            ];
            }
            """;

        var lines = await Harness.LinesAsync(CreateAnalyzer(), source);

        Assert.Equal([6, 7], lines);
    }

    [Fact]
    public async Task WholeLevelsAreNotReported()
    {
        const string source = """
            namespace N;

            class C
            {
                public static int[] Values { get; } = [
                    1,
                    2,
                ];
            }
            """;

        Assert.Empty(await Harness.LinesAsync(CreateAnalyzer(), source));
    }

    [Fact]
    public async Task ABlankLineCarriesNoIndentation()
    {
        const string source = "namespace N;\n\n   \nclass C\n{\n}\n";

        Assert.Empty(await Harness.LinesAsync(CreateAnalyzer(), source));
    }

    [Fact]
    public async Task TheInsideOfARawStringIsItsValue()
    {
        const string source = """"
            namespace N;

            class C
            {
                const string S = """
              two spaces here are data
                """;
            }
            """";

        Assert.Empty(await Harness.LinesAsync(CreateAnalyzer(), source));
    }

    [Fact]
    public async Task TheInsideOfABlockCommentIsNotLayout()
    {
        const string source = """
            namespace N;

            class C
            {
                /* a block
              comment continued at two
                 */
            }
            """;

        Assert.Empty(await Harness.LinesAsync(CreateAnalyzer(), source));
    }
}

using System.Threading.Tasks;
using Xunit;

namespace __ROOT_NAMESPACE__.Analyzers.Tests;

public sealed class EmptyInterfaceBodyAnalyzerTests
{
    private static EmptyInterfaceBodyAnalyzer CreateAnalyzer()
    {
        return new EmptyInterfaceBodyAnalyzer();
    }

    [Fact]
    public async Task EmptyBody_IsReported()
    {
        const string source = """
            namespace N;

            public interface IMarker
            {
            }
            """;

        var lines = await Harness.LinesAsync(CreateAnalyzer(), source);

        Assert.Equal([3], lines);
    }

    [Fact]
    public async Task SemicolonBody_IsNotReported()
    {
        const string source = """
            namespace N;

            public interface IMarker;
            """;

        Assert.Empty(await Harness.LinesAsync(CreateAnalyzer(), source));
    }

    [Fact]
    public async Task ABaseListIsNotAMember()
    {
        const string source = """
            namespace N;

            public interface IOne;

            public interface ITwo : IOne
            {
            }
            """;

        var lines = await Harness.LinesAsync(CreateAnalyzer(), source);

        Assert.Equal([5], lines);
    }

    [Fact]
    public async Task AConstraintIsNotAMember()
    {
        const string source = """
            namespace N;

            public interface IOne;

            public interface IHandler<TRequest> : IOne
                where TRequest : IOne
            {
            }
            """;

        var lines = await Harness.LinesAsync(CreateAnalyzer(), source);

        Assert.Equal([5], lines);
    }

    [Fact]
    public async Task AnInterfaceWithMembers_IsNotReported()
    {
        const string source = """
            namespace N;

            public interface IWork
            {
                void Do();
            }
            """;

        Assert.Empty(await Harness.LinesAsync(CreateAnalyzer(), source));
    }

    [Fact]
    public async Task ACommentBetweenTheBraces_IsLeftAlone()
    {
        const string source = """
            namespace N;

            public interface IMarker
            {
                // nothing here yet
            }
            """;

        Assert.Empty(await Harness.LinesAsync(CreateAnalyzer(), source));
    }

    [Fact]
    public async Task AnEmptyClass_IsNotThisRule()
    {
        const string source = """
            namespace N;

            public sealed class C
            {
            }
            """;

        Assert.Empty(await Harness.LinesAsync(CreateAnalyzer(), source));
    }
}

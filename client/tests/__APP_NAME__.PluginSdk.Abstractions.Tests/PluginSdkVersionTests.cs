using Xunit;

namespace __ROOT_NAMESPACE__.PluginSdk.Abstractions.Tests;

/// <summary>
/// Unit tests for <see cref="PluginSdkVersion"/>: what parses, what does not, and that the
/// ordering is numeric rather than the string order a naive comparison would give.
/// </summary>
public sealed class PluginSdkVersionTests
{
    [Theory]
    [InlineData("1.0", 1, 0)]
    [InlineData("2.11", 2, 11)]
    [InlineData("0.1", 0, 1)]
    public void TryParse_MajorMinor_Succeeds(string text, int major, int minor)
    {
        var parsed = PluginSdkVersion.TryParse(text, out var version);

        Assert.True(parsed);
        Assert.Equal(new PluginSdkVersion(major, minor), version);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("1")]
    [InlineData("1.2.3")]
    [InlineData("v1.2")]
    [InlineData("1.-2")]
    [InlineData("1.2 ")]
    [InlineData(null)]
    public void TryParse_AnythingElse_Fails(string? text)
    {
        var parsed = PluginSdkVersion.TryParse(text, out var version);

        Assert.False(parsed);
        Assert.Equal(default, version);
    }

    [Fact]
    public void Compare_OrdersByNumber_NotByText()
    {
        var lower = new PluginSdkVersion(1, 9);
        var higher = new PluginSdkVersion(1, 10);

        Assert.True(lower < higher);
        Assert.True(higher > lower);
        Assert.True(lower <= new PluginSdkVersion(1, 9));
        Assert.True(higher >= lower);
    }

    [Fact]
    public void Compare_MajorWins()
    {
        Assert.True(new PluginSdkVersion(2, 0) > new PluginSdkVersion(1, 99));
    }

    [Fact]
    public void ToString_IsWhatTryParseReads()
    {
        var version = new PluginSdkVersion(3, 4);

        var parsed = PluginSdkVersion.TryParse(version.ToString(), out var round);

        Assert.True(parsed);
        Assert.Equal(version, round);
    }

    [Fact]
    public void Current_IsThisAssemblysMajorMinor()
    {
        var expected = typeof(PluginSdkVersion).Assembly.GetName().Version;

        Assert.NotNull(expected);
        Assert.Equal(new PluginSdkVersion(expected.Major, expected.Minor), PluginSdkVersion.Current);
    }
}

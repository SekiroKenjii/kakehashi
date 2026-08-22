using System.IO;
using System.Linq;
using __ROOT_NAMESPACE__.PluginSdk.Abstractions;
using Xunit;

namespace __ROOT_NAMESPACE__.PluginSdk.Abstractions.Tests;

/// <summary>
/// Unit tests for <see cref="PluginAssembly"/>, read against this test assembly's own file.
/// </summary>
/// <remarks>
/// Its own file because the questions are about metadata, and an assembly the build just produced
/// is a real one — a hand-built byte array would only prove the reader agrees with the fixture.
/// The types it matches on are in Fixtures/, which says why they are declared there.
/// </remarks>
public sealed class PluginAssemblyTests
{
    private const string _fixtures = "__ROOT_NAMESPACE__.PluginSdk.Abstractions.Tests.Fixtures.";

    private static PluginAssembly Read()
    {
        using var file = File.OpenRead(typeof(PluginAssemblyTests).Assembly.Location);
        var read = PluginAssembly.Read(file);

        Assert.True(read.IsSuccess);

        return read.Value;
    }

    [Fact]
    public void PageTypes_FindsWhatDerivesFromThePageType()
    {
        var pages = Read().PageTypes;

        Assert.Contains(_fixtures + "WeatherPage", pages);
        Assert.Contains(_fixtures + "Forecast", pages);
        Assert.DoesNotContain(_fixtures + "WeatherSettings", pages);
    }

    [Fact]
    public void DeclaresXaml_IsTrueWhereTheMarkupProviderIsImplemented()
    {
        Assert.True(Read().DeclaresXaml);
    }

    [Fact]
    public void DeclaresModule_IsTrueOnlyForWhatImplementsTheHostsInterface()
    {
        var assembly = Read();

        Assert.True(assembly.DeclaresModule(_fixtures + "WeatherModule"));
        Assert.False(assembly.DeclaresModule(_fixtures + "WeatherSettings"));
    }

    /// <summary>
    /// The two refusals a packaging tool has to tell apart: a name that is not in the assembly is a
    /// spelling mistake, and a name that is but is not a module is a missing interface.
    /// </summary>
    [Fact]
    public void Declares_SeparatesATypoFromAMissingInterface()
    {
        var assembly = Read();

        Assert.False(assembly.Declares(_fixtures + "WeatherModul"));
        Assert.True(assembly.Declares(_fixtures + "WeatherSettings"));
        Assert.False(assembly.DeclaresModule(_fixtures + "WeatherSettings"));
    }

    [Fact]
    public void Read_AFileThatIsNotAnAssemblyIsRefusedRatherThanThrown()
    {
        using var stream = new MemoryStream([.. Enumerable.Repeat((byte)0x7A, 512)]);

        Assert.True(PluginAssembly.Read(stream).IsFailure);
    }
}

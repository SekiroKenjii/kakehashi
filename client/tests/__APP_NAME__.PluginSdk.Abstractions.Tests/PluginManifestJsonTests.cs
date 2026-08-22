using System.IO;
using System.Text;
using Xunit;

namespace __ROOT_NAMESPACE__.PluginSdk.Abstractions.Tests;

/// <summary>
/// Unit tests for <see cref="PluginManifestJson"/>: the round trip, the wire spelling of the
/// property names, and the two leniencies a hand-edited manifest needs.
/// </summary>
public sealed class PluginManifestJsonTests
{
    private static PluginManifest? ReadText(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        return PluginManifestJson.Read(stream);
    }

    private static string WriteText(PluginManifest manifest)
    {
        using var stream = new MemoryStream();
        PluginManifestJson.Write(stream, manifest);

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    [Fact]
    public void RoundTrip_PreservesEveryField()
    {
        var manifest = PluginManifests.Valid();

        var round = ReadText(WriteText(manifest));

        Assert.NotNull(round);
        Assert.Equal(manifest.SchemaVersion, round.SchemaVersion);
        Assert.Equal(manifest.Id, round.Id);
        Assert.Equal(manifest.ModuleName, round.ModuleName);
        Assert.Equal(manifest.DisplayName, round.DisplayName);
        Assert.Equal(manifest.Description, round.Description);
        Assert.Equal(manifest.Version, round.Version);
        Assert.Equal(manifest.Author, round.Author);
        Assert.Equal(manifest.Homepage, round.Homepage);
        Assert.Equal(manifest.EntryAssembly, round.EntryAssembly);
        Assert.Equal(manifest.ModuleType, round.ModuleType);
        Assert.Equal(manifest.PriFiles, round.PriFiles);
        Assert.Equal(manifest.MinHostSdk, round.MinHostSdk);
        Assert.Equal(manifest.Navigation, round.Navigation);
        Assert.Equal(manifest.CallsPermission, round.CallsPermission);
        Assert.Equal(manifest.RequiresUnsafeXamlHooks, round.RequiresUnsafeXamlHooks);
    }

    [Fact]
    public void Write_UsesCamelCaseOnTheWire()
    {
        var json = WriteText(PluginManifests.Valid());

        Assert.Contains("\"schemaVersion\"", json, System.StringComparison.Ordinal);
        Assert.Contains("\"entryAssembly\"", json, System.StringComparison.Ordinal);
        Assert.Contains("\"minHostSdk\"", json, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Read_ToleratesCommentsAndTrailingCommas()
    {
        var manifest = ReadText(
            """
            {
              // the identity the catalog keys on
              "schemaVersion": 1,
              "id": "weather",
              "moduleName": "Weather",
            }
            """);

        Assert.NotNull(manifest);
        Assert.Equal("weather", manifest.Id);
    }

    [Fact]
    public void Read_MissingFields_AreEmptyRatherThanNull()
    {
        var manifest = ReadText("""{ "schemaVersion": 1, "id": "weather" }""");

        Assert.NotNull(manifest);
        Assert.Empty(manifest.PriFiles);
        Assert.Empty(manifest.Navigation);
        Assert.Equal(string.Empty, manifest.DisplayName);
        Assert.Equal(string.Empty, manifest.CallsPermission);
        Assert.Equal(string.Empty, manifest.MinHostSdk);
    }

    [Fact]
    public void Read_ExplicitNulls_AreEmptyRatherThanNull()
    {
        var manifest = ReadText(
            """
            {
              "schemaVersion": 1,
              "displayName": null,
              "priFiles": null,
              "navigation": [{ "title": null, "page": null }]
            }
            """);

        Assert.NotNull(manifest);
        Assert.Equal(string.Empty, manifest.DisplayName);
        Assert.Empty(manifest.PriFiles);
        Assert.Equal(string.Empty, manifest.Navigation[0].Title);
        Assert.Equal(string.Empty, manifest.Navigation[0].Page);
    }

    [Fact]
    public void Validate_ManifestThatOmitsEverything_DoesNotThrow()
    {
        var manifest = ReadText("{}");

        Assert.NotNull(manifest);
        Assert.NotEmpty(PluginManifestValidator.Validate(manifest));
    }

    [Fact]
    public void Read_JsonThatIsNotAnObject_ReturnsNull()
    {
        Assert.Null(ReadText("null"));
    }
}

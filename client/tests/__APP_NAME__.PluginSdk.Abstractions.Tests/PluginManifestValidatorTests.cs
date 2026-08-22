using System.Collections.Generic;
using System.Linq;
using __ROOT_NAMESPACE__.SharedKernel;
using Xunit;

namespace __ROOT_NAMESPACE__.PluginSdk.Abstractions.Tests;

/// <summary>
/// Unit tests for <see cref="PluginManifestValidator"/>. Each case changes one field of a valid
/// manifest, so a failure names the rule that fired rather than the manifest that was wrong.
/// </summary>
public sealed class PluginManifestValidatorTests
{
    private static IReadOnlyList<string> Codes(IReadOnlyList<Error> errors)
    {
        return [.. errors.Select(error => error.Code)];
    }

    [Fact]
    public void Validate_ValidManifest_ReturnsNothing()
    {
        Assert.Empty(PluginManifestValidator.Validate(PluginManifests.Valid()));
    }

    [Fact]
    public void Validate_UnsupportedSchemaVersion_IsRefused()
    {
        var manifest = PluginManifests.Valid() with { SchemaVersion = 99 };

        Assert.Contains("Plugin.Manifest.SchemaVersionUnsupported", Codes(PluginManifestValidator.Validate(manifest)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Weather")]
    [InlineData("weather editor")]
    [InlineData("weather--editor")]
    [InlineData("-weather")]
    [InlineData("weather-")]
    public void Validate_IdThatIsNotLowerKebab_IsRefused(string id)
    {
        var manifest = PluginManifests.Valid() with { Id = id };

        Assert.Contains("Plugin.Manifest.IdInvalid", Codes(PluginManifestValidator.Validate(manifest)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("1Weather")]
    [InlineData("Weather.UI")]
    [InlineData("Weather Editor")]
    public void Validate_ModuleNameThatIsNotAnIdentifier_IsRefused(string moduleName)
    {
        var manifest = PluginManifests.Valid() with { ModuleName = moduleName };

        Assert.Contains("Plugin.Manifest.ModuleNameInvalid", Codes(PluginManifestValidator.Validate(manifest)));
    }

    [Fact]
    public void Validate_MissingDisplayName_IsRefused()
    {
        var manifest = PluginManifests.Valid() with { DisplayName = string.Empty };

        Assert.Contains("Plugin.Manifest.Required", Codes(PluginManifestValidator.Validate(manifest)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("1.0")]
    [InlineData("1.0.0.0")]
    [InlineData("1.0.0-beta")]
    [InlineData("v1.0.0")]
    public void Validate_VersionThatIsNotMajorMinorPatch_IsRefused(string version)
    {
        var manifest = PluginManifests.Valid() with { Version = version };

        Assert.Contains("Plugin.Manifest.VersionInvalid", Codes(PluginManifestValidator.Validate(manifest)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("lib/Weather.dll")]
    [InlineData("..\\Weather.dll")]
    public void Validate_EntryAssemblyThatIsAPath_IsRefused(string entryAssembly)
    {
        var manifest = PluginManifests.Valid() with { EntryAssembly = entryAssembly };

        Assert.Contains("Plugin.Manifest.FileNameInvalid", Codes(PluginManifestValidator.Validate(manifest)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("2Weather.Module")]
    [InlineData("Weather..Module")]
    public void Validate_ModuleTypeThatIsNotADottedIdentifier_IsRefused(string moduleType)
    {
        var manifest = PluginManifests.Valid() with { ModuleType = moduleType };

        Assert.Contains("Plugin.Manifest.TypeNameInvalid", Codes(PluginManifestValidator.Validate(manifest)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("1.2.3")]
    public void Validate_MinHostSdkThatIsNotMajorMinor_IsRefused(string minHostSdk)
    {
        var manifest = PluginManifests.Valid() with { MinHostSdk = minHostSdk };

        Assert.Contains("Plugin.Manifest.MinHostSdkInvalid", Codes(PluginManifestValidator.Validate(manifest)));
    }

    [Theory]
    [InlineData("Weather")]
    [InlineData("Page")]
    [InlineData("")]
    public void Validate_PageNameTheNavigationServiceWouldThrowOn_IsRefused(string page)
    {
        var manifest = PluginManifests.Valid() with {
            Navigation = new List<PluginNavigationEntry> { new() { Title = "Weather", Page = page } },
        };

        Assert.Contains("Plugin.Manifest.PageNameInvalid", Codes(PluginManifestValidator.Validate(manifest)));
    }

    [Fact]
    public void Validate_ReportsEveryProblemAtOnce()
    {
        var manifest = PluginManifests.Valid() with {
            Id = "Weather",
            Version = "one",
            MinHostSdk = string.Empty,
        };

        Assert.Equal(3, PluginManifestValidator.Validate(manifest).Count);
    }

    [Fact]
    public void CheckHost_HostNewerThanRequired_Succeeds()
    {
        var manifest = PluginManifests.Valid() with { MinHostSdk = "1.1" };

        Assert.True(PluginManifestValidator.CheckHost(manifest, new PluginSdkVersion(1, 2)).IsSuccess);
    }

    [Fact]
    public void CheckHost_HostOlderThanRequired_Fails()
    {
        var manifest = PluginManifests.Valid() with { MinHostSdk = "2.0" };

        var result = PluginManifestValidator.CheckHost(manifest, new PluginSdkVersion(1, 9));

        Assert.True(result.IsFailure);
        Assert.Equal("Plugin.Compatibility.HostTooOld", result.Error.Code);
    }

    [Fact]
    public void CheckHost_UnreadableMinimum_Fails()
    {
        var manifest = PluginManifests.Valid() with { MinHostSdk = "next" };

        var result = PluginManifestValidator.CheckHost(manifest, new PluginSdkVersion(9, 9));

        Assert.True(result.IsFailure);
        Assert.Equal("Plugin.Manifest.MinHostSdkInvalid", result.Error.Code);
    }
}

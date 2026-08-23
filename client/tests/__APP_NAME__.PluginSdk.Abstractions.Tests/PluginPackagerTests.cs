using System;
using System.IO;
using System.Security.Cryptography;
using __ROOT_NAMESPACE__.PluginSdk.Abstractions;
using Xunit;

namespace __ROOT_NAMESPACE__.PluginSdk.Abstractions.Tests;

/// <summary>
/// Unit tests for <see cref="PluginPackager"/>: that what it writes is a package the reader accepts,
/// and that it refuses a project there is nothing to pack yet.
/// </summary>
/// <remarks>
/// The one assertion that matters is the round trip. A writer whose output its own reader rejects
/// would produce files that pass on the author's machine and fail at install, which is the failure
/// the two halves exist together to prevent.
/// </remarks>
public sealed class PluginPackagerTests : IDisposable
{
    private const string _entryAssembly = "App.Modules.Weather.UI.dll";

    private readonly string _root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string Project => Path.Combine(_root, "project");

    private void WriteProject(bool withOutput = true)
    {
        Directory.CreateDirectory(Project);
        File.WriteAllText(Path.Combine(Project, PluginPackage.ManifestEntryName), Manifest);

        if (!withOutput)
        {
            return;
        }
        var output = Path.Combine(Project, "bin", "x64", "Debug");
        Directory.CreateDirectory(output);
        File.WriteAllBytes(Path.Combine(output, _entryAssembly), [0x4D, 0x5A]);
        File.WriteAllText(Path.Combine(output, "App.Modules.Weather.UI.pri"), "index");
    }

    private static string Manifest => """
        {
          "schemaVersion": 1,
          "id": "weather",
          "moduleName": "Weather",
          "displayName": "Weather",
          "version": "1.0.0",
          "entryAssembly": "App.Modules.Weather.UI.dll",
          "moduleType": "App.Modules.Weather.UI.WeatherModule",
          "priFiles": ["App.Modules.Weather.UI.pri"],
          "minHostSdk": "1.0"
        }
        """;

    [Fact]
    public void Pack_WritesTheArchiveUnderTheIdentityTheManifestDeclares()
    {
        WriteProject();

        var packed = PluginPackager.Pack(Project, _root);

        Assert.True(packed.IsSuccess);
        Assert.Equal("weather-1.0.0" + PluginPackage.Extension, Path.GetFileName(packed.Value));
        Assert.True(File.Exists(packed.Value));
    }

    [Fact]
    public void Pack_WhatItWritesIsAPackageTheReaderOpens()
    {
        WriteProject();
        var packed = PluginPackager.Pack(Project, _root);

        using var opened = PluginPackage.Open(packed.Value).Value;

        Assert.Equal("weather", opened.Manifest.Id);
        Assert.Contains(_entryAssembly, opened.LibraryFiles);
        Assert.Empty(opened.Validate());
    }

    /// <summary>Beside the package, so an editor of the package is not also the editor of its digest.</summary>
    [Fact]
    public void Pack_TheDigestBesideItIsTheDigestOfWhatItWrote()
    {
        WriteProject();
        var packed = PluginPackager.Pack(Project, _root);
        var sidecar = File.ReadAllText(packed.Value + PluginPackager.DigestExtension);
        var expected = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(packed.Value)));

        Assert.StartsWith(expected, sidecar, StringComparison.Ordinal);
        Assert.Contains(Path.GetFileName(packed.Value), sidecar, StringComparison.Ordinal);
    }

    [Fact]
    public void Pack_AProjectThatHasNotBeenBuiltIsRefusedByName()
    {
        WriteProject(withOutput: false);

        var packed = PluginPackager.Pack(Project, _root);

        Assert.True(packed.IsFailure);
        Assert.Contains(_entryAssembly, packed.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_ADirectoryWithNoManifestIsRefused()
    {
        Directory.CreateDirectory(Project);

        Assert.True(PluginPackager.Build(Project).IsFailure);
    }
}

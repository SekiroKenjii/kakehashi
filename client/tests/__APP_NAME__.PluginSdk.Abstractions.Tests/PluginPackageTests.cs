using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Xunit;

namespace __ROOT_NAMESPACE__.PluginSdk.Abstractions.Tests;

/// <summary>
/// Unit tests for <see cref="PluginPackage"/>. The fixtures are built with
/// <see cref="ZipArchive"/> directly rather than by this code, so the reader is tested against an
/// archive it did not write — which is the case that matters, since every real package comes from
/// somewhere else.
/// </summary>
public sealed class PluginPackageTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "plugin-package-tests", Path.GetRandomFileName());

    private static MemoryStream BuildArchive(
        PluginManifest? manifest, IEnumerable<string>? entries = null)
    {
        var stream = new MemoryStream();

        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            if (manifest is not null)
            {
                var manifestEntry = archive.CreateEntry(PluginPackage.ManifestEntryName);
                using var content = manifestEntry.Open();
                PluginManifestJson.Write(content, manifest);
            }

            foreach (var name in entries ?? DefaultEntries())
            {
                var entry = archive.CreateEntry(name);
                using var content = entry.Open();
                content.Write(Encoding.UTF8.GetBytes(name));
            }
        }
        stream.Position = 0;

        return stream;
    }

    private static IEnumerable<string> DefaultEntries()
    {
        yield return PluginPackage.LibraryFolder + PluginManifests.EntryAssembly;
        yield return PluginPackage.LibraryFolder + PluginManifests.PriFile;
    }

    private static PluginPackage OpenValid(MemoryStream stream)
    {
        var opened = PluginPackage.Open(stream, leaveOpen: true);

        Assert.True(opened.IsSuccess);

        return opened.Value;
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void Open_ReadsTheManifest()
    {
        using var stream = BuildArchive(PluginManifests.Valid());

        using var package = OpenValid(stream);

        Assert.Equal("weather", package.Manifest.Id);
    }

    [Fact]
    public void Open_WithoutAManifest_Fails()
    {
        using var stream = BuildArchive(manifest: null);

        var opened = PluginPackage.Open(stream, leaveOpen: true);

        Assert.True(opened.IsFailure);
        Assert.Equal("Plugin.Package.ManifestMissing", opened.Error.Code);
    }

    [Fact]
    public void Open_SomethingThatIsNotAnArchive_Fails()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("not a zip"));

        var opened = PluginPackage.Open(stream, leaveOpen: true);

        Assert.True(opened.IsFailure);
        Assert.Equal("Plugin.Package.Unreadable", opened.Error.Code);
    }

    [Fact]
    public void Open_ManifestThatIsNotJson_Fails()
    {
        var stream = new MemoryStream();

        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry(PluginPackage.ManifestEntryName);
            using var content = entry.Open();
            content.Write(Encoding.UTF8.GetBytes("{ this is not json"));
        }
        stream.Position = 0;

        var opened = PluginPackage.Open(stream, leaveOpen: true);

        Assert.True(opened.IsFailure);
        Assert.Equal("Plugin.Package.ManifestUnreadable", opened.Error.Code);
    }

    [Fact]
    public void Validate_WellFormedPackage_ReturnsNothing()
    {
        using var stream = BuildArchive(PluginManifests.Valid());

        using var package = OpenValid(stream);

        Assert.Empty(package.Validate());
    }

    [Fact]
    public void Validate_MissingEntryAssembly_IsRefused()
    {
        using var stream = BuildArchive(
            PluginManifests.Valid(), [PluginPackage.LibraryFolder + PluginManifests.PriFile]);

        using var package = OpenValid(stream);

        Assert.Contains(package.Validate(), error => error.Code == "Plugin.Package.FileMissing");
    }

    [Fact]
    public void Validate_MissingResourceIndex_IsRefused()
    {
        using var stream = BuildArchive(
            PluginManifests.Valid(), [PluginPackage.LibraryFolder + PluginManifests.EntryAssembly]);

        using var package = OpenValid(stream);

        var missing = package
            .Validate()
            .Where(error => error.Code == "Plugin.Package.FileMissing");

        Assert.Single(missing);
    }

    [Fact]
    public void Validate_WindowsRuntimeComponent_IsRefused()
    {
        var entries = DefaultEntries().Append(PluginPackage.LibraryFolder + "Weather.winmd");
        using var stream = BuildArchive(PluginManifests.Valid(), entries);

        using var package = OpenValid(stream);

        Assert.Contains(
            package.Validate(), error => error.Code == "Plugin.Package.WindowsRuntimeComponent");
    }

    [Fact]
    public void Validate_CarriesTheManifestsOwnProblems()
    {
        using var stream = BuildArchive(PluginManifests.Valid() with { Id = "Weather" });

        using var package = OpenValid(stream);

        Assert.Contains(package.Validate(), error => error.Code == "Plugin.Manifest.IdInvalid");
    }

    [Fact]
    public void ExtractTo_WritesEveryFile()
    {
        using var stream = BuildArchive(PluginManifests.Valid());
        using var package = OpenValid(stream);
        Directory.CreateDirectory(_directory);

        var result = package.ExtractTo(_directory);

        Assert.True(result.IsSuccess);
        Assert.True(File.Exists(Path.Combine(_directory, PluginPackage.ManifestEntryName)));
        Assert.True(File.Exists(Path.Combine(_directory, "lib", PluginManifests.EntryAssembly)));
    }

    [Fact]
    public void ExtractTo_EntryThatClimbsOutOfTheDirectory_IsRefused()
    {
        var entries = DefaultEntries().Append("../escaped.dll");
        using var stream = BuildArchive(PluginManifests.Valid(), entries);
        using var package = OpenValid(stream);
        Directory.CreateDirectory(_directory);

        var result = package.ExtractTo(_directory);

        Assert.True(result.IsFailure);
        Assert.Equal("Plugin.Package.PathEscapes", result.Error.Code);
        Assert.False(File.Exists(Path.Combine(_directory, "..", "escaped.dll")));
    }
}

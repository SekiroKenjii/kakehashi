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

    /// <summary>Two entries of one name, which the archive allows and the file system does not.</summary>
    private static MemoryStream BuildArchiveWithTwoManifests()
    {
        var stream = new MemoryStream();

        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var body in new[] { "first", "second" })
            {
                var entry = archive.CreateEntry(PluginPackage.ManifestEntryName);

                using var content = entry.Open();
                content.Write(Encoding.UTF8.GetBytes(body));
            }
        }
        stream.Position = 0;

        return stream;
    }

    /// <summary>
    /// The attack this refusal exists for: GetEntry answers with the first manifest and extraction
    /// writes the last, so a package could be judged as one thing and load as another.
    /// </summary>
    [Fact]
    public void Open_WithTwoManifests_IsRefused()
    {
        using var stream = BuildArchiveWithTwoManifests();

        var opened = PluginPackage.Open(stream, leaveOpen: true);

        Assert.True(opened.IsFailure);
        Assert.Equal("Plugin.Package.EntryNameRepeated", opened.Error.Code);
    }

    /// <summary>
    /// The same aliasing by another route. Each of these resolves to a path inside the destination,
    /// so the guard against climbing out of it never fires — the names have to be refused as names.
    /// </summary>
    [Theory]
    [InlineData("lib/../manifest.json")]
    [InlineData("./manifest.json")]
    [InlineData("lib//x.dll")]
    [InlineData(@"lib\x.dll")]
    [InlineData("/lib/x.dll")]
    [InlineData("C:/lib/x.dll")]
    [InlineData("lib/x.dll.")]
    [InlineData("lib/x.dll ")]
    [InlineData("lib./x.dll")]
    [InlineData("lib/x\u0000.dll")]
    public void Open_WithANameThatDoesNotMeanOneFile_IsRefused(string name)
    {
        using var stream = BuildArchive(PluginManifests.Valid(), [.. DefaultEntries(), name]);

        var opened = PluginPackage.Open(stream, leaveOpen: true);

        Assert.True(opened.IsFailure);
        Assert.Equal("Plugin.Package.EntryNameInvalid", opened.Error.Code);
    }

    /// <summary>
    /// Ordinal uniqueness is not enough: the archive tells these apart and the file system does not.
    /// </summary>
    [Fact]
    public void Open_WithTwoLibraryFilesDifferingOnlyInCase_IsRefused()
    {
        using var stream = BuildArchive(
            PluginManifests.Valid(),
            [.. DefaultEntries(), PluginPackage.LibraryFolder + PluginManifests.EntryAssembly.ToUpperInvariant()]);

        var opened = PluginPackage.Open(stream, leaveOpen: true);

        Assert.True(opened.IsFailure);
        Assert.Equal("Plugin.Package.EntryNameRepeated", opened.Error.Code);
    }

    /// <summary>A directory entry ends in a slash, and that is not a doubled separator.</summary>
    [Fact]
    public void Open_WithADirectoryEntry_IsAccepted()
    {
        using var stream = BuildArchive(PluginManifests.Valid(), [.. DefaultEntries(), "assets/"]);

        Assert.True(PluginPackage.Open(stream, leaveOpen: true).IsSuccess);
    }

    /// <summary>
    /// A name the archive accepts and Windows will not take. Refused rather than thrown, because
    /// the caller is a click handler with a dialog open.
    /// </summary>
    [Fact]
    public void ExtractTo_AnEntryThisSystemCannotWrite_IsARefusalRatherThanAThrow()
    {
        using var stream = BuildArchive(PluginManifests.Valid(), [.. DefaultEntries(), "lib/a?b.dll"]);

        var opened = PluginPackage.Open(stream, leaveOpen: true);

        Assert.True(opened.IsSuccess);

        using var package = opened.Value;
        Directory.CreateDirectory(_directory);

        var extracted = package.ExtractTo(_directory);

        Assert.True(extracted.IsFailure);
        Assert.Equal("Plugin.Package.EntryUnwritable", extracted.Error.Code);
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

    /// <summary>
    /// A name that climbs out is now refused a step earlier, when the archive is opened, because it
    /// is not a name that means one file. The guard in <c>ExtractTo</c> is unreachable through
    /// <c>Open</c> and stays because the method is public.
    /// </summary>
    [Fact]
    public void Open_WithAnEntryThatClimbsOutOfTheDirectory_IsRefused()
    {
        var entries = DefaultEntries().Append("../escaped.dll");

        using var stream = BuildArchive(PluginManifests.Valid(), entries);

        var opened = PluginPackage.Open(stream, leaveOpen: true);

        Assert.True(opened.IsFailure);
        Assert.Equal("Plugin.Package.EntryNameInvalid", opened.Error.Code);
    }
}

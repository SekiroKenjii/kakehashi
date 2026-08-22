using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using __ROOT_NAMESPACE__.SharedKernel;

namespace __ROOT_NAMESPACE__.PluginSdk.Abstractions;

/// <summary>
/// A plugin package: a zip holding <c>manifest.json</c> at its root and the module's assemblies
/// under <c>lib/</c>.
/// </summary>
/// <remarks>
/// Nothing here executes plugin code. Reading a package, deciding whether to trust it, and showing
/// the user what it declares all happen before a single byte of it is loaded, which is the whole
/// point of the manifest being a file rather than an attribute.
/// </remarks>
public sealed class PluginPackage : IDisposable
{
    /// <summary>The file extension a packed plugin carries.</summary>
    public const string Extension = ".plugin";

    public const string ManifestEntryName = "manifest.json";

    public const string LibraryFolder = "lib/";

    /// <summary>
    /// What the entries may expand to. A zip that decompresses to far more than it costs to send
    /// is the oldest trick there is, and the check has to be against the declared sizes rather
    /// than against what has already been written to disk.
    /// </summary>
    public const long MaxTotalBytes = 128L * 1024 * 1024;

    public const int MaxEntryCount = 4096;

    private const string _windowsRuntimeExtension = ".winmd";

    private readonly ZipArchive _archive;

    private PluginPackage(ZipArchive archive, PluginManifest manifest)
    {
        _archive = archive;
        Manifest = manifest;
    }

    public PluginManifest Manifest { get; }

    public static Result<PluginPackage> Open(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        FileStream stream;

        try
        {
            stream = File.OpenRead(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Result.Failure<PluginPackage>(PluginErrors.PackageUnreadable);
        }

        var opened = Open(stream, leaveOpen: false);

        if (opened.IsFailure)
        {
            stream.Dispose();
        }

        return opened;
    }

    public static Result<PluginPackage> Open(Stream stream, bool leaveOpen)
    {
        ArgumentNullException.ThrowIfNull(stream);

        ZipArchive archive;

        try
        {
            archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen);
        }
        catch (InvalidDataException)
        {
            return Result.Failure<PluginPackage>(PluginErrors.PackageUnreadable);
        }

        var read = ReadManifest(archive);

        if (read.IsFailure)
        {
            archive.Dispose();

            return Result.Failure<PluginPackage>(read.Error);
        }

        return new PluginPackage(archive, read.Value);
    }

    /// <summary>
    /// Every reason this package would be refused: what the manifest says wrong, and what it names
    /// that the archive does not hold.
    /// </summary>
    public IReadOnlyList<Error> Validate()
    {
        var errors = new List<Error>(PluginManifestValidator.Validate(Manifest));

        if (_archive.Entries.Count > MaxEntryCount)
        {
            errors.Add(PluginErrors.TooManyEntries(_archive.Entries.Count, MaxEntryCount));
        }
        var total = _archive.Entries.Sum(entry => entry.Length);

        if (total > MaxTotalBytes)
        {
            errors.Add(PluginErrors.PackageTooLarge(total, MaxTotalBytes));
        }

        if (HasWindowsRuntimeComponent())
        {
            errors.Add(PluginErrors.WindowsRuntimeComponent);
        }
        AddMissingFileErrors(errors);

        return errors;
    }

    /// <summary>Unpacks into a directory that must already exist and be empty.</summary>
    /// <remarks>
    /// Each entry's resolved path is checked against the destination before it is written, so an
    /// entry whose name climbs out of the directory is refused rather than followed.
    /// </remarks>
    public Result ExtractTo(string destinationDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(destinationDirectory);
        var root = Path.GetFullPath(destinationDirectory);
        var prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;

        foreach (var entry in _archive.Entries)
        {
            if (entry.FullName.EndsWith('/'))
            {
                continue;
            }
            var target = Path.GetFullPath(Path.Combine(root, entry.FullName));

            if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return Result.Failure(PluginErrors.PathEscapes(entry.FullName));
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }

        return Result.Success();
    }

    public void Dispose()
    {
        _archive.Dispose();
    }

    private static Result<PluginManifest> ReadManifest(ZipArchive archive)
    {
        var entry = archive.GetEntry(ManifestEntryName);

        if (entry is null)
        {
            return Result.Failure<PluginManifest>(PluginErrors.ManifestMissing);
        }

        try
        {
            using var stream = entry.Open();
            var manifest = PluginManifestJson.Read(stream);

            return manifest is null
                ? Result.Failure<PluginManifest>(PluginErrors.ManifestUnreadable)
                : Result.Success(manifest);
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or InvalidDataException)
        {
            return Result.Failure<PluginManifest>(PluginErrors.ManifestUnreadable);
        }
    }

    private bool HasWindowsRuntimeComponent()
    {
        return _archive.Entries.Any(
            entry => entry.FullName.EndsWith(_windowsRuntimeExtension, StringComparison.OrdinalIgnoreCase));
    }

    private void AddMissingFileErrors(List<Error> errors)
    {
        var entryAssembly = LibraryFolder + Manifest.EntryAssembly;

        if (_archive.GetEntry(entryAssembly) is null)
        {
            errors.Add(PluginErrors.FileMissing(entryAssembly));
        }

        foreach (var priFile in Manifest.PriFiles)
        {
            var path = LibraryFolder + priFile;

            if (_archive.GetEntry(path) is null)
            {
                errors.Add(PluginErrors.FileMissing(path));
            }
        }
    }
}

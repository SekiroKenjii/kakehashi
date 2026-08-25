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
    /// <summary>The file extension a packed plugin carries, chosen when the project was made.</summary>
    public const string Extension = ".__PLUGIN_EXT__";

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

    /// <summary>Everything under lib/, named the way the manifest names it.</summary>
    public IReadOnlyList<string> LibraryFiles => [.. _archive.Entries
        .Select(entry => entry.FullName)
        .Where(name => name.StartsWith(LibraryFolder, StringComparison.Ordinal) && !name.EndsWith('/'))
        .Select(name => name[LibraryFolder.Length..])];

    /// <summary>
    /// Reads one file out of lib/, or nothing where the package does not hold it.
    /// </summary>
    /// <remarks>
    /// Into memory, because a compressed entry's stream cannot seek and every reader of one of
    /// these needs to. An assembly is small; the size of the whole archive is capped elsewhere.
    /// </remarks>
    public MemoryStream? ReadLibraryFile(string name)
    {
        var entry = _archive.GetEntry(LibraryFolder + name);

        if (entry is null)
        {
            return null;
        }
        var copy = new MemoryStream();

        using (var content = entry.Open())
        {
            content.CopyTo(copy);
        }
        copy.Position = 0;

        return copy;
    }

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
        var names = CheckNamespace(archive);

        if (names.IsFailure)
        {
            archive.Dispose();

            return Result.Failure<PluginPackage>(names.Error);
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

    /// <summary>
    /// Refuses an archive whose entry names do not each mean one file.
    /// </summary>
    /// <remarks>
    /// Read before the manifest is, because everything downstream asks the archive for an entry by
    /// name and gets the first, while extraction writes them all and the last wins on disk. Two
    /// entries called manifest.json are a package that is judged as one thing and loads as another.
    /// <para>
    /// The names are compared as they are written, never resolved first: resolving is what makes
    /// <c>lib/../manifest.json</c> land inside the destination and pass the guard in
    /// <see cref="ExtractTo"/>.
    /// </para>
    /// </remarks>
    private static Result CheckNamespace(ZipArchive archive)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in archive.Entries.Select(entry => entry.FullName))
        {
            if (!IsCanonical(name))
            {
                return Result.Failure(PluginErrors.EntryNameInvalid(name));
            }

            // Ordinally unique is not enough: GetEntry matches ordinally and the file system does
            // not, so lib/X.dll and lib/x.dll are two entries and one file.
            if (!seen.Add(name))
            {
                return Result.Failure(PluginErrors.EntryNameRepeated(name));
            }
        }

        return Result.Success();
    }

    private static bool IsCanonical(string name)
    {
        if (name.Length == 0 || name.StartsWith('/') || name.Contains('\\') || name.Contains(':'))
        {
            return false;
        }

        // A control character is never part of a name somebody meant, and a NUL reaches
        // Path.GetFullPath before any filter downstream can catch what it throws.
        if (name.Any(char.IsControl))
        {
            return false;
        }
        var segments = name.Split('/');

        for (var i = 0; i < segments.Length; i++)
        {
            if (segments[i] is "." or "..")
            {
                return false;
            }

            // A directory entry ends in a slash, so its last segment is empty by design. Anywhere
            // else an empty segment is a doubled separator.
            if (segments[i].Length == 0 && i != segments.Length - 1)
            {
                return false;
            }

            // Windows drops a trailing dot or space, so 'x.dll' and 'x.dll.' are one file on disk
            // and two names here — the aliasing the uniqueness rule above exists to refuse.
            if (segments[i].Length > 0 && segments[i][^1] is '.' or ' ')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Unpacks into a directory, creating what each entry needs under it.</summary>
    /// <remarks>
    /// Each entry's resolved path is checked against the destination before it is written, so an
    /// entry whose name climbs out of the directory is refused rather than followed. An archive
    /// opened through <see cref="Open(Stream, bool)"/> holds no such name, and the check stays
    /// because the method is public.
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

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, overwrite: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                or ArgumentException or NotSupportedException)
            {
                // A name Windows will not take — a wildcard character, a reserved device name.
                // Refused here rather than thrown at whoever is holding the dialog open.
                return Result.Failure(PluginErrors.EntryUnwritable(entry.FullName));
            }
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

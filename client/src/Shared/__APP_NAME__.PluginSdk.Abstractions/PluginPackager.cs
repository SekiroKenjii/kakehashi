using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using __ROOT_NAMESPACE__.SharedKernel;

namespace __ROOT_NAMESPACE__.PluginSdk.Abstractions;

/// <summary>
/// Turns a built plugin project into the archive the application installs.
/// </summary>
/// <remarks>
/// The writing half of <see cref="PluginPackage"/>, and separate from it on purpose: that type is
/// a reader over an archive somebody else produced, and its tests are worth more for being written
/// against archives it did not build.
/// <para>
/// <see cref="Build"/> is what makes checking a project mean the same thing as checking a package.
/// Everything that inspects a project goes through it, so what an author is told about their
/// project is what a user would be told about the file it produces.
/// </para>
/// </remarks>
public static class PluginPackager
{
    /// <summary>What a digest is written beside the package in.</summary>
    public const string DigestExtension = ".sha256";

    /// <summary>
    /// Packs a project's build output, and returns the file it wrote.
    /// </summary>
    /// <param name="projectDirectory">The directory holding manifest.json.</param>
    /// <param name="outputDirectory">Where to write. Created if it is not there.</param>
    public static Result<string> Pack(string projectDirectory, string outputDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(projectDirectory);
        ArgumentException.ThrowIfNullOrEmpty(outputDirectory);
        var manifest = ReadManifest(projectDirectory);

        if (manifest.IsFailure)
        {
            return Result.Failure<string>(manifest.Error);
        }
        var built = Build(projectDirectory);

        if (built.IsFailure)
        {
            return Result.Failure<string>(built.Error);
        }

        using var archive = built.Value;
        var path = Path.Combine(
            outputDirectory,
            $"{manifest.Value.Id}-{manifest.Value.Version}{PluginPackage.Extension}");

        try
        {
            Directory.CreateDirectory(outputDirectory);
            File.WriteAllBytes(path, archive.ToArray());
            archive.Position = 0;

            // Beside the package rather than inside it: a digest a package carries is one an
            // editor of that package can rewrite. sha256sum reads this shape.
            File.WriteAllText(path + DigestExtension, $"{Digest(archive)}  {Path.GetFileName(path)}\n");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Result.Failure<string>(PluginErrors.OutputUnwritable(exception.Message));
        }

        return Result.Success(path);
    }

    /// <summary>
    /// Builds the archive in memory: manifest.json at the root, the build output under lib/.
    /// </summary>
    /// <remarks>
    /// Everything in the output directory goes in. Choosing which of an author's files their
    /// plugin needs is a judgement this has no basis for, and dropping one is a failure that shows
    /// up as a missing type at navigation time rather than here.
    /// </remarks>
    public static Result<MemoryStream> Build(string projectDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(projectDirectory);
        var manifest = ReadManifest(projectDirectory);

        if (manifest.IsFailure)
        {
            return Result.Failure<MemoryStream>(manifest.Error);
        }
        var output = FindOutput(projectDirectory, manifest.Value.EntryAssembly);

        if (output.IsFailure)
        {
            return Result.Failure<MemoryStream>(output.Error);
        }
        var stream = new MemoryStream();

        try
        {
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            {
                archive.CreateEntryFromFile(
                    Path.Combine(projectDirectory, PluginPackage.ManifestEntryName),
                    PluginPackage.ManifestEntryName);

                foreach (var file in Directory.EnumerateFiles(output.Value, "*", SearchOption.AllDirectories))
                {
                    var relative = Path
                        .GetRelativePath(output.Value, file)
                        .Replace('\\', '/');
                    archive.CreateEntryFromFile(file, PluginPackage.LibraryFolder + relative);
                }
            }
            stream.Position = 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            stream.Dispose();

            return Result.Failure<MemoryStream>(PluginErrors.OutputUnwritable(exception.Message));
        }

        return Result.Success(stream);
    }

    /// <summary>Lower-case hex SHA-256 of everything left in the stream.</summary>
    public static string Digest(Stream content)
    {
        ArgumentNullException.ThrowIfNull(content);

        return Convert.ToHexStringLower(SHA256.HashData(content));
    }

    private static Result<PluginManifest> ReadManifest(string projectDirectory)
    {
        var path = Path.Combine(projectDirectory, PluginPackage.ManifestEntryName);

        if (!File.Exists(path))
        {
            return Result.Failure<PluginManifest>(PluginErrors.ProjectManifestMissing(projectDirectory));
        }

        try
        {
            using var file = File.OpenRead(path);
            var manifest = PluginManifestJson.Read(file);

            return manifest is null
                ? Result.Failure<PluginManifest>(PluginErrors.ManifestUnreadable)
                : Result.Success(manifest);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Result.Failure<PluginManifest>(PluginErrors.ManifestUnreadable);
        }
    }

    /// <summary>
    /// The build output, found by the assembly the manifest names rather than by a path convention.
    /// </summary>
    /// <remarks>
    /// Newest wins, because a project built for more than one configuration or platform has the
    /// entry assembly under several of them and the one somebody just built is the one they mean.
    /// </remarks>
    private static Result<string> FindOutput(string projectDirectory, string entryAssembly)
    {
        if (entryAssembly.Length == 0)
        {
            return Result.Failure<string>(PluginErrors.Required(nameof(PluginManifest.EntryAssembly)));
        }

        try
        {
            var found = Directory
                .EnumerateFiles(projectDirectory, entryAssembly, SearchOption.AllDirectories)
                .Where(path => path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();

            return found is null
                ? Result.Failure<string>(PluginErrors.BuildOutputMissing(entryAssembly, projectDirectory))
                : Result.Success(Path.GetDirectoryName(found)!);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Result.Failure<string>(PluginErrors.BuildOutputMissing(entryAssembly, projectDirectory));
        }
    }
}

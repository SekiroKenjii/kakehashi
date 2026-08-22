using System;
using System.IO;
using __ROOT_NAMESPACE__.PluginSdk.Abstractions;
using __ROOT_NAMESPACE__.SharedKernel;

namespace __ROOT_NAMESPACE__.App.Plugins;

/// <summary>What a package turned out to be, once it was opened and judged.</summary>
/// <param name="Manifest">What it declares about itself.</param>
/// <param name="Trust">How far it is trusted, and by what evidence.</param>
/// <param name="SizeInBytes">The size of the package file.</param>
/// <param name="StagedDirectory">Where it was unpacked while the decision is made.</param>
public sealed record PluginPreview(
    PluginManifest Manifest, PluginTrustVerdict Trust, long SizeInBytes, string StagedDirectory);

/// <summary>
/// Puts a package where the next launch will find it.
/// </summary>
/// <remarks>
/// Installing never touches what is loaded. The files go to a staging directory and the state file
/// records what is waiting; the loader promotes it at the start of the next launch, which is the
/// one moment nothing holds the previous version open.
/// <para>
/// It is two steps because the user is asked in between. <see cref="Inspect"/> unpacks far enough
/// to say what the package is and who signed it, and <see cref="Commit"/> is what that answer is
/// given to.
/// </para>
/// </remarks>
public sealed class PluginInstaller
{
    private readonly PluginPaths _paths;
    private readonly string _publisher;
    private readonly Func<DateTimeOffset> _now;

    public PluginInstaller(PluginPaths paths, string publisher, Func<DateTimeOffset>? now = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _paths = paths;
        _publisher = publisher;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Opens a package, checks everything that can be checked, and judges how far it is trusted.
    /// </summary>
    /// <remarks>
    /// It unpacks, because the signature is on the assembly inside rather than on the archive, and
    /// nothing about the file itself can answer who signed the code. What is unpacked is left in
    /// the staging directory for <see cref="Commit"/>, or removed by <see cref="Discard"/>.
    /// </remarks>
    public Result<PluginPreview> Inspect(string packagePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(packagePath);
        var opened = PluginPackage.Open(packagePath);

        if (opened.IsFailure)
        {
            return Result.Failure<PluginPreview>(opened.Error);
        }

        using var package = opened.Value;
        var problems = package.Validate();

        if (problems.Count > 0)
        {
            return Result.Failure<PluginPreview>(problems[0]);
        }
        var manifest = package.Manifest;
        var supported = PluginManifestValidator.CheckHost(manifest, PluginSdkVersion.Current);

        if (supported.IsFailure)
        {
            return Result.Failure<PluginPreview>(supported.Error);
        }
        var staged = _paths.StagedDirectory(manifest.Id, manifest.Version);
        Clear(staged);

        try
        {
            Directory.CreateDirectory(staged);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Result.Failure<PluginPreview>(PluginLoadErrors.DirectoryMissing(staged));
        }
        var extracted = package.ExtractTo(staged);

        if (extracted.IsFailure)
        {
            Clear(staged);

            return Result.Failure<PluginPreview>(extracted.Error);
        }
        var entry = Path.Combine(
            staged, PluginPackage.LibraryFolder.TrimEnd('/'), manifest.EntryAssembly);
        var trust = PluginTrust.Judge(packagePath, entry, _publisher);
        var size = new FileInfo(packagePath).Length;

        return Result.Success(new PluginPreview(manifest, trust, size, staged));
    }

    /// <summary>
    /// Accepts an inspected package, so the next launch loads it.
    /// </summary>
    /// <param name="preview">What <see cref="Inspect"/> returned.</param>
    /// <param name="consented">
    /// Whether the user accepted the risk. Required for anything not verified, and ignored for what
    /// is: a package this application's own publisher signed is not something to be asked about.
    /// </param>
    /// <param name="source">Where the package came from, for the row that reports it later.</param>
    public Result Commit(PluginPreview preview, bool consented, string source)
    {
        ArgumentNullException.ThrowIfNull(preview);

        if (preview.Trust.Level != PluginTrustLevel.Verified && !consented)
        {
            Discard(preview);

            return Result.Failure(PluginLoadErrors.Invalid(
                "This package is not verified, and installing it was not agreed to."));
        }
        var state = PluginState.Load(_paths);
        var record = state.Find(preview.Manifest.Id) ?? new PluginRecord { PluginID = preview.Manifest.Id };

        record.DisplayName = preview.Manifest.DisplayName;
        record.StagedVersion = preview.Manifest.Version;
        record.PendingRemove = false;
        record.Source = source;
        record.SHA256 = preview.Trust.SHA256;
        record.SignerSubject = preview.Trust.Signer;
        record.Signature = preview.Trust.Level.ToString();
        record.ConsentGiven = consented;
        record.SizeInBytes = preview.SizeInBytes;
        record.InstalledOn = _now();
        state.Put(record);

        if (!state.TrySave())
        {
            Discard(preview);

            return Result.Failure(PluginLoadErrors.DirectoryMissing(_paths.StateFile));
        }

        return Result.Success();
    }

    /// <summary>Throws away what <see cref="Inspect"/> unpacked.</summary>
    public void Discard(PluginPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        Clear(preview.StagedDirectory);
    }

    /// <summary>
    /// Marks a plugin for removal. The files come out at the start of the next launch, before
    /// anything opens them.
    /// </summary>
    public Result Uninstall(string pluginID)
    {
        ArgumentException.ThrowIfNullOrEmpty(pluginID);
        var state = PluginState.Load(_paths);
        var record = state.Find(pluginID);

        if (record is null)
        {
            return Result.Failure(PluginLoadErrors.DirectoryMissing(_paths.InstalledRoot(pluginID)));
        }
        record.PendingRemove = true;
        record.StagedVersion = string.Empty;
        state.Put(record);

        return state.TrySave()
            ? Result.Success()
            : Result.Failure(PluginLoadErrors.DirectoryMissing(_paths.StateFile));
    }

    /// <summary>Whether the user has already agreed to exactly this package.</summary>
    /// <remarks>
    /// Against the identity and the digest, which together settle it: different bytes are a
    /// different package whatever its version says, so an update is a fresh decision.
    /// </remarks>
    public static bool AlreadyConsented(PluginRecord? record, PluginPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);

        return record is not null
            && record.ConsentGiven
            && record.PluginID.Equals(preview.Manifest.Id, StringComparison.Ordinal)
            && record.SHA256.Equals(preview.Trust.SHA256, StringComparison.Ordinal);
    }

    private static void Clear(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Nothing here is load-bearing: a staging directory that survives is overwritten by the
            // next inspection of the same version.
        }
    }
}

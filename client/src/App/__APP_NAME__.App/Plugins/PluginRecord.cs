using System;

namespace __ROOT_NAMESPACE__.App.Plugins;

/// <summary>What this installation knows about one plugin, whether or not it is loaded.</summary>
/// <remarks>
/// Properties are settable rather than init-only because this record is deserialized by the JSON
/// source generator, which assigns every mapped property — including the ones the document omits —
/// and so discards what an init-only property's initializer would have provided.
/// </remarks>
public sealed record PluginRecord
{
    /// <summary>The catalog identity, which is also the directory name.</summary>
    public string PluginID { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    /// <summary>What the application loads today. Empty while an install waits for a restart.</summary>
    public string InstalledVersion { get; set; } = string.Empty;

    /// <summary>What it will load next launch. Empty when there is nothing waiting.</summary>
    public string StagedVersion { get; set; } = string.Empty;

    /// <summary>
    /// Set by an uninstall. The files are still on disk and still loaded, so they come out at the
    /// start of the next launch, before anything opens them.
    /// </summary>
    public bool PendingRemove { get; set; }

    /// <summary>Where the package came from: a catalog, a URL, or a file the user already had.</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>The digest of the package as installed, which is half of what consent was given for.</summary>
    public string SHA256 { get; set; } = string.Empty;

    public string SignerSubject { get; set; } = string.Empty;

    /// <summary>The trust verdict at install time, as a name rather than a number.</summary>
    public string Signature { get; set; } = string.Empty;

    /// <summary>
    /// Whether the user accepted the risk of an unverified package.
    /// </summary>
    /// <remarks>
    /// Keyed by the identity, the version and the digest together: change any of the three and the
    /// package is not the one that was agreed to, so the question is asked again.
    /// </remarks>
    public bool ConsentGiven { get; set; }

    public long SizeInBytes { get; set; }

    public DateTimeOffset InstalledOn { get; set; }
}

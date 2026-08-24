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
    /// What the trust provider actually said, which the verdict collapses.
    /// </summary>
    /// <remarks>
    /// Unsigned and modified-since-signed are both Unofficial and are not the same news, so the row
    /// and the prompt read this rather than inferring from the verdict.
    /// </remarks>
    public string SignatureStatus { get; set; } = string.Empty;

    /// <summary>
    /// Whether the user accepted the risk of an unverified package.
    /// </summary>
    /// <remarks>
    /// Keyed by the identity and the digest together: different bytes are a different package
    /// whatever its version says, so the question is asked again.
    /// </remarks>
    public bool ConsentGiven { get; set; }

    /// <summary>
    /// The pane heading this plugin's screens sit under, chosen on this machine.
    /// </summary>
    /// <remarks>
    /// Empty means whatever the module itself said. It is stored here rather than arranged by the
    /// deployment because a plugin is installed on one machine and the deployment has no row for
    /// it — a heading it cannot see is not one it can be asked to arrange.
    /// </remarks>
    public string Group { get; set; } = string.Empty;

    public long SizeInBytes { get; set; }

    public DateTimeOffset InstalledOn { get; set; }
}

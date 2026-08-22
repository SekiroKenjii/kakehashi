using System;
using System.IO;

namespace __ROOT_NAMESPACE__.App.Plugins;

/// <summary>
/// Where installed plugins live, and where one waits while the running application still holds the
/// version it replaces.
/// </summary>
/// <remarks>
/// Under the user's local application data rather than beside the executable, which may sit
/// somewhere only an administrator can write. Installing is then something a user can do to their
/// own copy of the application without needing anybody's permission, which is the whole point.
/// <para>
/// The root is a constructor argument so a test can point at a temporary directory; the production
/// value is <see cref="Default"/>.
/// </para>
/// </remarks>
public sealed class PluginPaths
{
    /// <summary>The extension a packed plugin carries.</summary>
    public const string PackageExtension = ".plugin";

    private const string _installedFolder = "installed";
    private const string _stagedFolder = "staged";
    private const string _stateFile = "state.json";

    public PluginPaths(string root)
    {
        ArgumentException.ThrowIfNullOrEmpty(root);
        Root = root;
        Installed = Path.Combine(root, _installedFolder);
        Staged = Path.Combine(root, _stagedFolder);
        StateFile = Path.Combine(root, _stateFile);
    }

    /// <summary>
    /// The per-user location, which is the same root the settings file already uses.
    /// </summary>
    public static PluginPaths Default { get; } = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "__APP_NAME__",
        "Plugins"));

    public string Root { get; }

    /// <summary>What the application loads from.</summary>
    public string Installed { get; }

    /// <summary>What it will load from after the next launch.</summary>
    public string Staged { get; }

    public string StateFile { get; }

    public string InstalledDirectory(string pluginID, string version)
    {
        return Path.Combine(Installed, pluginID, version);
    }

    public string StagedDirectory(string pluginID, string version)
    {
        return Path.Combine(Staged, pluginID, version);
    }

    /// <summary>Every directory a plugin has ever been installed into, for removal.</summary>
    public string InstalledRoot(string pluginID)
    {
        return Path.Combine(Installed, pluginID);
    }

    public string StagedRoot(string pluginID)
    {
        return Path.Combine(Staged, pluginID);
    }
}

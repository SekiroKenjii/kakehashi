using System;
using System.Collections.Generic;
using System.Linq;
using __ROOT_NAMESPACE__.PluginSdk.Abstractions;
using __ROOT_NAMESPACE__.SharedKernel;

namespace __ROOT_NAMESPACE__.App.Plugins;

/// <summary>A plugin this launch loaded.</summary>
/// <param name="Record">What the installation knows about it.</param>
/// <param name="Manifest">What the package declares.</param>
public sealed record LoadedPlugin(PluginRecord Record, PluginManifest Manifest);

/// <summary>A plugin this launch refused, and why.</summary>
/// <param name="PluginID">The identity from the state file, which is all a refusal is sure of.</param>
/// <param name="Version">The version that was tried.</param>
/// <param name="Reason">What went wrong, in words a row can carry.</param>
public sealed record PluginFault(string PluginID, string Version, Error Reason);

/// <summary>
/// What became of the installed plugins at startup, for the screen that reports it.
/// </summary>
/// <remarks>
/// The loader runs before the container exists, so it cannot log through the application's logger
/// and cannot show anything. It records instead, and whoever can — a logger once the host is built,
/// the plugins page once somebody opens it — reads this.
/// </remarks>
public sealed class PluginCatalog
{
    private readonly List<LoadedPlugin> _loaded = [];
    private readonly List<PluginFault> _faults = [];
    private readonly List<PluginRecord> _staged = [];

    /// <summary>The plugins whose modules are part of this composition.</summary>
    public IReadOnlyList<LoadedPlugin> Loaded => _loaded;

    /// <summary>The ones that could not be loaded, each with a reason worth showing.</summary>
    public IReadOnlyList<PluginFault> Faults => _faults;

    /// <summary>Installs and removals that take effect at the next launch.</summary>
    public IReadOnlyList<PluginRecord> AwaitingRestart => _staged;

    /// <summary>Whether anything is waiting, which is what the banner asks.</summary>
    public bool RestartRequired => _staged.Count > 0;

    public void Add(LoadedPlugin plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        _loaded.Add(plugin);
    }

    public void AddFault(string pluginID, string version, Error reason)
    {
        _faults.Add(new PluginFault(pluginID, version, reason));
    }

    public void AddAwaitingRestart(PluginRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        _staged.Add(record);
    }

    /// <summary>The module names this composition owes to plugins, for the screen that lists them.</summary>
    public IReadOnlyList<string> ModuleNames()
    {
        return [.. _loaded.Select(plugin => plugin.Manifest.ModuleName)];
    }
}

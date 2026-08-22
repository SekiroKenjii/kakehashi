using __ROOT_NAMESPACE__.SharedKernel;

namespace __ROOT_NAMESPACE__.App.Plugins;

/// <summary>Why a plugin did not load, or would not install.</summary>
public static class PluginLoadErrors
{
    public static readonly Error Disabled = new(
        "Plugin.Load.Disabled", "Plugins are turned off for this installation.");

    public static readonly Error PackagedHost = new(
        "Plugin.Load.PackagedHost",
        "Plugins are supported only when the application is installed unpackaged.");

    public static Error DirectoryMissing(string path)
    {
        return new Error("Plugin.Load.DirectoryMissing", $"Nothing is installed at '{path}'.");
    }

    public static Error ManifestUnreadable(string path)
    {
        return new Error("Plugin.Load.ManifestUnreadable", $"The manifest at '{path}' could not be read.");
    }

    public static Error Invalid(string reason)
    {
        return new Error("Plugin.Load.Invalid", reason);
    }

    public static Error AssemblyUnloadable(string name, string reason)
    {
        return new Error("Plugin.Load.AssemblyUnloadable", $"'{name}' could not be loaded: {reason}");
    }

    public static Error ModuleTypeMissing(string typeName)
    {
        return new Error("Plugin.Load.ModuleTypeMissing", $"'{typeName}' is not a module in this package.");
    }

    public static Error ModuleNameMismatch(string declared, string actual)
    {
        return new Error(
            "Plugin.Load.ModuleNameMismatch",
            $"The manifest names the module '{declared}', and it calls itself '{actual}'.");
    }

    /// <summary>
    /// The navigation service derives a page's key by dropping a "Page" suffix and registers it in
    /// a table where the last writer wins, so a plugin whose page shares a key with one already
    /// registered would replace that screen rather than add its own.
    /// </summary>
    public static Error PageKeyTaken(string key)
    {
        return new Error(
            "Plugin.Load.PageKeyTaken",
            $"This build already has a screen called '{key}', and a plugin may not replace one.");
    }

    /// <summary>
    /// A module's registration is additive. Removing or replacing something the host registered
    /// would let a plugin substitute its own navigation, its own token store, its own anything.
    /// </summary>
    public static Error RegistrationRemovedServices(string moduleName)
    {
        return new Error(
            "Plugin.Load.RegistrationRemovedServices",
            $"'{moduleName}' tried to remove or replace a service this application registered.");
    }
}

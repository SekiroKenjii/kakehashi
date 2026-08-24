using System;
using __ROOT_NAMESPACE__.SharedKernel;

namespace __ROOT_NAMESPACE__.App.Plugins;

/// <summary>Why a plugin did not load, or would not install.</summary>
public static class PluginLoadErrors
{
    public static Error DirectoryMissing(string path)
    {
        return new Error("Plugin.Load.DirectoryMissing", $"Nothing is installed at '{path}'.");
    }

    /// <summary>
    /// The staged version is still where it was, so the next launch tries again — which is why this
    /// says what failed rather than that something is missing.
    /// </summary>
    public static Error PromoteFailed(string version, string reason)
    {
        return new Error(
            "Plugin.Load.PromoteFailed",
            $"Version {version} could not be put in place and is still staged: {reason}");
    }

    public static Error AlreadyStaged(string version)
    {
        return new Error(
            "Plugin.Install.AlreadyStaged",
            $"Version {version} is already staged and loads at the next launch. Restart, or remove it first.");
    }

    /// <summary>
    /// The manifest that was read out of the archive and the one that landed on disk are not the
    /// same package, so what was judged is not what would be loaded.
    /// </summary>
    public static Error ManifestDisagrees(string pluginID)
    {
        return new Error(
            "Plugin.Install.ManifestDisagrees",
            $"'{pluginID}' unpacked to a different package than it declared.");
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
    /// Attachment is keyed by module name, so two modules of one name share a toggle and the second
    /// hides the first.
    /// </summary>
    public static Error ModuleNameTaken(string moduleName)
    {
        return new Error(
            "Plugin.Load.ModuleNameTaken",
            $"This build already has a module called '{moduleName}'.");
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
    /// A module's registration is additive. Removing something the host registered would let a
    /// plugin substitute its own navigation, its own token store, its own anything.
    /// </summary>
    public static Error RegistrationRemovedServices(string moduleName)
    {
        return new Error(
            "Plugin.Load.RegistrationRemovedServices",
            $"'{moduleName}' tried to remove a service this application registered.");
    }

    /// <summary>
    /// Registering a service the host already registered is how a plugin substitutes one without
    /// removing anything: the container resolves the last descriptor, and a plugin registers last.
    /// </summary>
    public static Error RegistrationShadowedService(string moduleName, string serviceType)
    {
        return new Error(
            "Plugin.Load.RegistrationShadowedService",
            $"'{moduleName}' registered its own '{serviceType}', which this application already provides.");
    }

    /// <summary>
    /// Post-configuration runs after the host's own and needs no removal to win, so reaching into
    /// the options of a type the plugin does not own is substitution by another name.
    /// </summary>
    public static Error RegistrationReachedHostOptions(string moduleName, string serviceType)
    {
        return new Error(
            "Plugin.Load.RegistrationReachedHostOptions",
            $"'{moduleName}' registered '{serviceType}', which configures options it does not own.");
    }

    /// <summary>
    /// A plugin's own code threw. Named here rather than allowed to escape, because a plugin that
    /// stops the application from starting is the outcome the whole design exists to avoid.
    /// </summary>
    public static Error Threw(string member, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return new Error("Plugin.Load.Threw", $"{member} threw {exception.GetType().Name}: {exception.Message}");
    }
}

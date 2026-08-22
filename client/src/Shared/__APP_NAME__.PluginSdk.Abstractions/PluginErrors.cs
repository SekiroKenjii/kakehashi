using System.Globalization;
using __ROOT_NAMESPACE__.SharedKernel;

namespace __ROOT_NAMESPACE__.PluginSdk.Abstractions;

/// <summary>
/// Why a package was refused. The packaging tool prints these and the host shows them on the
/// plugin's row, so each one names the offending value rather than the rule it broke.
/// </summary>
public static class PluginErrors
{
    public static readonly Error ManifestMissing = new(
        "Plugin.Package.ManifestMissing", "The package has no manifest.json at its root.");

    public static readonly Error ManifestUnreadable = new(
        "Plugin.Package.ManifestUnreadable", "manifest.json is not valid JSON.");

    public static readonly Error PackageUnreadable = new(
        "Plugin.Package.Unreadable", "The package is not a readable archive.");

    /// <summary>
    /// A Windows Runtime component would work unpackaged and fail packaged: a packaged app's
    /// activatable classes come from its own manifest, which a plugin cannot extend.
    /// </summary>
    public static readonly Error WindowsRuntimeComponent = new(
        "Plugin.Package.WindowsRuntimeComponent",
        "The package contains a .winmd. Plugins may ship managed assemblies only.");

    public static readonly Error AssemblyUnreadable = new(
        "Plugin.Content.AssemblyUnreadable", "That file is not a managed assembly.");

    public static Error SchemaVersionUnsupported(int declared, int supported)
    {
        return new Error(
            "Plugin.Manifest.SchemaVersionUnsupported",
            string.Create(
                CultureInfo.InvariantCulture,
                $"Manifest schema version {declared} is not supported; this build reads {supported}."));
    }

    public static Error IdInvalid(string id)
    {
        return new Error(
            "Plugin.Manifest.IdInvalid",
            $"'{id}' is not a valid plugin id. Use lower case, digits and single hyphens.");
    }

    public static Error ModuleNameInvalid(string moduleName)
    {
        return new Error(
            "Plugin.Manifest.ModuleNameInvalid",
            $"'{moduleName}' is not a valid module name. Use PascalCase letters and digits.");
    }

    public static Error Required(string field)
    {
        return new Error(
            "Plugin.Manifest.Required", $"manifest.json is missing a value for '{field}'.");
    }

    public static Error FileNameInvalid(string field, string value)
    {
        return new Error(
            "Plugin.Manifest.FileNameInvalid",
            $"'{value}' is not a file name in lib/ for '{field}'.");
    }

    public static Error VersionInvalid(string version)
    {
        return new Error(
            "Plugin.Manifest.VersionInvalid",
            $"'{version}' is not a valid version. Use major.minor.patch.");
    }

    public static Error MinHostSdkInvalid(string minHostSdk)
    {
        return new Error(
            "Plugin.Manifest.MinHostSdkInvalid",
            $"'{minHostSdk}' is not a valid host SDK version. Use major.minor.");
    }

    public static Error TypeNameInvalid(string field, string typeName)
    {
        return new Error(
            "Plugin.Manifest.TypeNameInvalid", $"'{typeName}' is not a valid type name for '{field}'.");
    }

    /// <summary>
    /// The navigation service derives a page's key by dropping a "Page" suffix and throws when
    /// there is none — inside the shell, where nothing points at the cause.
    /// </summary>
    public static Error PageNameInvalid(string pageName)
    {
        return new Error(
            "Plugin.Manifest.PageNameInvalid", $"Page type '{pageName}' must end in 'Page'.");
    }

    public static Error FileMissing(string path)
    {
        return new Error(
            "Plugin.Package.FileMissing", $"The manifest names '{path}', which the package does not hold.");
    }

    /// <summary>
    /// An entry whose path climbs out of the destination directory, which extraction would
    /// otherwise follow.
    /// </summary>
    public static Error PathEscapes(string path)
    {
        return new Error(
            "Plugin.Package.PathEscapes", $"Entry '{path}' resolves outside the destination directory.");
    }

    public static Error PackageTooLarge(long bytes, long limit)
    {
        return new Error(
            "Plugin.Package.TooLarge",
            string.Create(
                CultureInfo.InvariantCulture,
                $"The package expands to {bytes} bytes, over the {limit}-byte limit."));
    }

    public static Error TooManyEntries(int count, int limit)
    {
        return new Error(
            "Plugin.Package.TooManyEntries",
            string.Create(
                CultureInfo.InvariantCulture, $"The package holds {count} entries, over the limit of {limit}."));
    }

    public static Error HostTooOld(PluginSdkVersion required, PluginSdkVersion host)
    {
        return new Error(
            "Plugin.Compatibility.HostTooOld",
            $"Requires host SDK v{required} — this build is v{host}.");
    }

    /// <summary>
    /// Only the entry assembly has to be readable. A package legitimately carries native libraries
    /// its build brought along, and refusing one of those would refuse a package that works.
    /// </summary>
    public static Error EntryAssemblyUnreadable(string file)
    {
        return new Error(
            "Plugin.Content.EntryAssemblyUnreadable",
            $"'{file}' is what the manifest names as the entry assembly, and it is not a managed one.");
    }

    public static Error ModuleTypeMissing(string typeName)
    {
        return new Error(
            "Plugin.Content.ModuleTypeMissing",
            $"The manifest names '{typeName}', which the entry assembly does not declare.");
    }

    /// <summary>
    /// The type is there and the host could not mount it, which is a different mistake from naming
    /// one that is not: the fix is an interface rather than a spelling.
    /// </summary>
    public static Error ModuleTypeNotAModule(string typeName)
    {
        return new Error(
            "Plugin.Content.ModuleTypeNotAModule", $"'{typeName}' does not implement IModule.");
    }

    /// <summary>
    /// The failure this catches is silent: the pages build, and then nothing can find them, because
    /// the index that holds their compiled markup was never packed.
    /// </summary>
    public static Error ResourceIndexUndeclared(string assembly)
    {
        return new Error(
            "Plugin.Content.ResourceIndexUndeclared",
            $"'{assembly}' carries compiled XAML, and no resource index for it is declared in priFiles.");
    }

    public static Error NavigationPageMissing(string pageName)
    {
        return new Error(
            "Plugin.Content.NavigationPageMissing",
            $"The manifest adds '{pageName}' to navigation, and no page of that name is in the package.");
    }

    /// <summary>
    /// A plugin's own strings do not resolve: the lookup walks resource subtrees, and a subtree
    /// miss raises nothing the host can answer — so the label renders empty in front of a user.
    /// </summary>
    public static Error LocalizedMarkup(string file)
    {
        return new Error(
            "Plugin.Content.LocalizedMarkup",
            $"'{file}' uses x:Uid. A plugin reads its own strings in code instead.");
    }

    public static Error ProjectManifestMissing(string directory)
    {
        return new Error(
            "Plugin.Project.ManifestMissing", $"'{directory}' has no manifest.json.");
    }

    public static Error BuildOutputMissing(string entryAssembly, string directory)
    {
        return new Error(
            "Plugin.Project.BuildOutputMissing",
            $"No '{entryAssembly}' under any bin/ in '{directory}'. Build the project first.");
    }

    public static Error OutputUnwritable(string reason)
    {
        return new Error("Plugin.Project.OutputUnwritable", reason);
    }
}

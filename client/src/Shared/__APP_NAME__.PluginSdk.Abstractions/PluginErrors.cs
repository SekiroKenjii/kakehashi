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
}

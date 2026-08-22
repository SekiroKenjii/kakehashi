using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using __ROOT_NAMESPACE__.SharedKernel;

namespace __ROOT_NAMESPACE__.PluginSdk.Abstractions;

/// <summary>
/// Everything that can be decided about a manifest without opening the assemblies it names.
/// </summary>
/// <remarks>
/// The packaging tool and the host loader both run this, so a package that passes on the author's
/// machine cannot be refused for a different reason on the user's. It returns every problem rather
/// than the first: an author fixing a manifest one refusal at a time is a slow way to learn the
/// schema.
/// </remarks>
public static partial class PluginManifestValidator
{
    /// <summary>The manifest schema this build reads.</summary>
    public const int SupportedSchemaVersion = 1;

    /// <summary>The suffix the navigation service derives a page's key by dropping.</summary>
    public const string PageSuffix = "Page";

    public static IReadOnlyList<Error> Validate(PluginManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var errors = new List<Error>();

        if (manifest.SchemaVersion != SupportedSchemaVersion)
        {
            errors.Add(PluginErrors.SchemaVersionUnsupported(manifest.SchemaVersion, SupportedSchemaVersion));
        }

        if (!IdPattern().IsMatch(manifest.Id))
        {
            errors.Add(PluginErrors.IdInvalid(manifest.Id));
        }

        if (!ModuleNamePattern().IsMatch(manifest.ModuleName))
        {
            errors.Add(PluginErrors.ModuleNameInvalid(manifest.ModuleName));
        }

        if (manifest.DisplayName.Length == 0)
        {
            errors.Add(PluginErrors.Required(nameof(manifest.DisplayName)));
        }

        if (!VersionPattern().IsMatch(manifest.Version))
        {
            errors.Add(PluginErrors.VersionInvalid(manifest.Version));
        }

        if (!FileNamePattern().IsMatch(manifest.EntryAssembly))
        {
            errors.Add(PluginErrors.FileNameInvalid(nameof(manifest.EntryAssembly), manifest.EntryAssembly));
        }

        if (!TypeNamePattern().IsMatch(manifest.ModuleType))
        {
            errors.Add(PluginErrors.TypeNameInvalid(nameof(manifest.ModuleType), manifest.ModuleType));
        }

        if (!PluginSdkVersion.TryParse(manifest.MinHostSdk, out _))
        {
            errors.Add(PluginErrors.MinHostSdkInvalid(manifest.MinHostSdk));
        }
        ValidatePriFiles(manifest, errors);
        ValidateNavigation(manifest, errors);

        return errors;
    }

    /// <summary>
    /// Whether this host is new enough. Checked before the assembly is loaded, so a plugin built
    /// against a later SDK is refused with a sentence instead of failing inside XAML.
    /// </summary>
    public static Result CheckHost(PluginManifest manifest, PluginSdkVersion host)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        if (!PluginSdkVersion.TryParse(manifest.MinHostSdk, out var required))
        {
            return Result.Failure(PluginErrors.MinHostSdkInvalid(manifest.MinHostSdk));
        }

        return required > host
            ? Result.Failure(PluginErrors.HostTooOld(required, host))
            : Result.Success();
    }

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex IdPattern();

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9]*$")]
    private static partial Regex ModuleNamePattern();

    [GeneratedRegex(@"^\d+\.\d+\.\d+$")]
    private static partial Regex VersionPattern();

    /// <summary>A name inside the package's lib folder, never a path.</summary>
    [GeneratedRegex(@"^[A-Za-z0-9._-]+$")]
    private static partial Regex FileNamePattern();

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*$")]
    private static partial Regex TypeNamePattern();

    private static void ValidatePriFiles(PluginManifest manifest, List<Error> errors)
    {
        foreach (var priFile in manifest.PriFiles)
        {
            if (!FileNamePattern().IsMatch(priFile))
            {
                errors.Add(PluginErrors.FileNameInvalid(nameof(manifest.PriFiles), priFile));
            }
        }
    }

    private static void ValidateNavigation(PluginManifest manifest, List<Error> errors)
    {
        foreach (var entry in manifest.Navigation)
        {
            if (entry.Title.Length == 0)
            {
                errors.Add(PluginErrors.Required(nameof(entry.Title)));
            }

            if (!TypeNamePattern().IsMatch(entry.Page)
                || !entry.Page.EndsWith(PageSuffix, StringComparison.Ordinal)
                || entry.Page.Length == PageSuffix.Length)
            {
                errors.Add(PluginErrors.PageNameInvalid(entry.Page));
            }
        }
    }
}

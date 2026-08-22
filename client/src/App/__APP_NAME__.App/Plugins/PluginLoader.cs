using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using __ROOT_NAMESPACE__.PluginSdk.Abstractions;
using __ROOT_NAMESPACE__.PluginSdk.Xaml;
using __ROOT_NAMESPACE__.SharedKernel;
using __ROOT_NAMESPACE__.UI.Contracts;

namespace __ROOT_NAMESPACE__.App.Plugins;

/// <summary>What one launch made of the installed plugins.</summary>
/// <param name="Modules">The modules to compose, in the order they loaded.</param>
/// <param name="Catalog">What loaded, what did not, and what is waiting for a restart.</param>
public sealed record PluginLoadResult(IReadOnlyList<IModule> Modules, PluginCatalog Catalog);

/// <summary>
/// Brings installed plugins into this composition.
/// </summary>
/// <remarks>
/// It runs during host construction, because a module's services must be registered while the
/// service collection is still open. That is also why installing takes effect at the next launch
/// rather than immediately, and why a removal happens here: this is the one moment the files are
/// not yet open.
/// <para>
/// Nothing in here throws. A plugin that cannot be loaded becomes a row with a reason on it, and
/// the application starts without it — an application that will not start because of a plugin is
/// the outcome the whole design exists to avoid.
/// </para>
/// </remarks>
public static class PluginLoader
{
    /// <summary>
    /// Settles what is installed, then loads it.
    /// </summary>
    /// <param name="paths">Where plugins live.</param>
    /// <param name="xaml">The bridge that makes a plugin's compiled XAML resolvable.</param>
    /// <param name="reservedPageKeys">
    /// The navigation keys this build already owns. A plugin claiming one of them is refused rather
    /// than allowed to replace the screen behind it.
    /// </param>
    public static PluginLoadResult LoadAll(
        PluginPaths paths, PluginXamlHost xaml, IReadOnlyCollection<string> reservedPageKeys)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(xaml);
        ArgumentNullException.ThrowIfNull(reservedPageKeys);

        var catalog = new PluginCatalog();
        var state = PluginState.Load(paths);
        Settle(paths, state, catalog);

        var taken = new HashSet<string>(reservedPageKeys, StringComparer.Ordinal);
        var modules = new List<IModule>();

        foreach (var record in state.Records)
        {
            if (record.InstalledVersion.Length == 0)
            {
                continue;
            }
            var loaded = Load(paths, xaml, record, taken, out var module);

            if (loaded.IsFailure)
            {
                catalog.AddFault(record.PluginID, record.InstalledVersion, loaded.Error);

                continue;
            }
            modules.Add(module!);
            catalog.Add(new LoadedPlugin(record, loaded.Value));
        }

        return new PluginLoadResult(modules, catalog);
    }

    /// <summary>
    /// The page keys a build owns before any plugin is loaded.
    /// </summary>
    /// <remarks>
    /// Derived the same way the navigation service derives them — the type name without its "Page"
    /// suffix — because a key that matched by a different rule would not be the one that collides.
    /// </remarks>
    public static IReadOnlyCollection<string> PageKeysOf(IEnumerable<NavigationItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        return [.. items
            .Select(item => KeyOf(item.PageType))
            .Where(key => key.Length > 0)];
    }

    /// <summary>
    /// Removes what an uninstall marked, and promotes what an install staged.
    /// </summary>
    /// <remarks>
    /// Both happen before a single assembly is loaded, which is the only moment the files are not
    /// held open. A failure here leaves the previous version in place and the staged copy where it
    /// was, so the next launch tries the same thing again rather than ending up with neither.
    /// </remarks>
    private static void Settle(PluginPaths paths, PluginState state, PluginCatalog catalog)
    {
        var changed = false;

        foreach (var record in state.Records)
        {
            if (record.PendingRemove)
            {
                Delete(paths.InstalledRoot(record.PluginID));
                Delete(paths.StagedRoot(record.PluginID));
                state.Remove(record.PluginID);
                changed = true;

                continue;
            }

            if (record.StagedVersion.Length == 0)
            {
                continue;
            }
            var staged = paths.StagedDirectory(record.PluginID, record.StagedVersion);
            var installed = paths.InstalledDirectory(record.PluginID, record.StagedVersion);

            if (!Promote(paths, record, staged, installed))
            {
                catalog.AddFault(
                    record.PluginID,
                    record.StagedVersion,
                    PluginLoadErrors.DirectoryMissing(staged));

                continue;
            }
            record.InstalledVersion = record.StagedVersion;
            record.StagedVersion = string.Empty;
            state.Put(record);
            changed = true;
        }

        if (changed)
        {
            _ = state.TrySave();
        }
    }

    private static bool Promote(PluginPaths paths, PluginRecord record, string staged, string installed)
    {
        if (!Directory.Exists(staged))
        {
            return false;
        }

        try
        {
            Delete(paths.InstalledRoot(record.PluginID));
            var parent = Path.GetDirectoryName(installed);

            if (parent is not null)
            {
                Directory.CreateDirectory(parent);
            }
            Directory.Move(staged, installed);
            Delete(paths.StagedRoot(record.PluginID));

            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static Result<PluginManifest> Load(
        PluginPaths paths,
        PluginXamlHost xaml,
        PluginRecord record,
        HashSet<string> taken,
        out IModule? module)
    {
        module = null;
        var directory = paths.InstalledDirectory(record.PluginID, record.InstalledVersion);

        if (!Directory.Exists(directory))
        {
            return Result.Failure<PluginManifest>(PluginLoadErrors.DirectoryMissing(directory));
        }
        var manifestPath = Path.Combine(directory, PluginPackage.ManifestEntryName);
        PluginManifest? manifest;

        try
        {
            using var stream = File.OpenRead(manifestPath);
            manifest = PluginManifestJson.Read(stream);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Result.Failure<PluginManifest>(PluginLoadErrors.ManifestUnreadable(manifestPath));
        }

        if (manifest is null)
        {
            return Result.Failure<PluginManifest>(PluginLoadErrors.ManifestUnreadable(manifestPath));
        }
        var problems = PluginManifestValidator.Validate(manifest);

        if (problems.Count > 0)
        {
            return Result.Failure<PluginManifest>(PluginLoadErrors.Invalid(problems[0].Message));
        }

        // Before the assembly is loaded, so a package built against a later SDK is a sentence on a
        // row rather than a type-load failure inside XAML.
        var supported = PluginManifestValidator.CheckHost(manifest, PluginSdkVersion.Current);

        if (supported.IsFailure)
        {
            return Result.Failure<PluginManifest>(supported.Error);
        }
        var library = Path.Combine(directory, PluginPackage.LibraryFolder.TrimEnd('/'));

        foreach (var priFile in manifest.PriFiles)
        {
            var added = xaml.AddPackage(Path.Combine(library, priFile));

            if (added.IsFailure)
            {
                return Result.Failure<PluginManifest>(added.Error);
            }
        }
        var assemblyPath = Path.Combine(library, manifest.EntryAssembly);
        Assembly assembly;

        try
        {
            assembly = Assembly.LoadFrom(assemblyPath);
        }
        catch (Exception exception)
            when (exception is IOException or BadImageFormatException or UnauthorizedAccessException)
        {
            return Result.Failure<PluginManifest>(
                PluginLoadErrors.AssemblyUnloadable(manifest.EntryAssembly, exception.Message));
        }
        var registered = xaml.AddMetadataProvider(assembly);

        if (registered.IsFailure)
        {
            return Result.Failure<PluginManifest>(registered.Error);
        }
        var created = Activate(assembly, manifest);

        if (created.IsFailure)
        {
            return Result.Failure<PluginManifest>(created.Error);
        }
        var reserved = ClaimPageKeys(created.Value, taken);

        if (reserved.IsFailure)
        {
            return Result.Failure<PluginManifest>(reserved.Error);
        }
        module = created.Value;

        return Result.Success(manifest);
    }

    private static Result<IModule> Activate(Assembly assembly, PluginManifest manifest)
    {
        try
        {
            if (assembly.GetType(manifest.ModuleType, throwOnError: false) is not { } type
                || !typeof(IModule).IsAssignableFrom(type)
                || type.GetConstructor(Type.EmptyTypes) is null)
            {
                return Result.Failure<IModule>(PluginLoadErrors.ModuleTypeMissing(manifest.ModuleType));
            }
            var module = (IModule)Activator.CreateInstance(type)!;

            return module.Name == manifest.ModuleName
                ? Result.Success(module)
                : Result.Failure<IModule>(
                    PluginLoadErrors.ModuleNameMismatch(manifest.ModuleName, module.Name));
        }
        catch (Exception exception) when (exception is TargetInvocationException or MissingMethodException
            or TypeLoadException or FileNotFoundException or BadImageFormatException)
        {
            return Result.Failure<IModule>(
                PluginLoadErrors.AssemblyUnloadable(manifest.ModuleType, exception.Message));
        }
    }

    /// <summary>
    /// Refuses a plugin whose pages would take a key this build already answers to, and reserves
    /// the ones it may have.
    /// </summary>
    private static Result ClaimPageKeys(IModule module, HashSet<string> taken)
    {
        var keys = new List<string>();

        foreach (var item in module.GetNavigationItems())
        {
            var key = KeyOf(item.PageType);

            if (key.Length == 0)
            {
                return Result.Failure(PluginLoadErrors.Invalid(
                    $"'{item.PageType.Name}' is not a page name the navigation service can key."));
            }

            if (taken.Contains(key))
            {
                return Result.Failure(PluginLoadErrors.PageKeyTaken(key));
            }
            keys.Add(key);
        }

        foreach (var key in keys)
        {
            _ = taken.Add(key);
        }

        return Result.Success();
    }

    private static string KeyOf(Type pageType)
    {
        const string suffix = "Page";
        var name = pageType.Name;

        return name.Length > suffix.Length && name.EndsWith(suffix, StringComparison.Ordinal)
            ? name[..^suffix.Length]
            : string.Empty;
    }

    private static void Delete(string directory)
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
            // A directory still held open comes out on a later launch. Failing here would strand
            // the application on a plugin it is trying to be rid of.
        }
    }
}

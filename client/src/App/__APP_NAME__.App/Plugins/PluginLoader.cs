using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using __ROOT_NAMESPACE__.PluginSdk.Abstractions;
using __ROOT_NAMESPACE__.PluginSdk.Xaml;
using __ROOT_NAMESPACE__.SharedKernel;
using __ROOT_NAMESPACE__.UI.Contracts;

namespace __ROOT_NAMESPACE__.App.Plugins;

/// <summary>A module a plugin contributed, with the identity it is filed under.</summary>
/// <remarks>
/// The id travels with the module because composition can still refuse one, and a refusal filed
/// under the module's name is a row the state file cannot be looked up by — the two vocabularies
/// differ, so uninstalling it would find nothing.
/// <para>
/// The catalog entry travels with it for the same reason: it is added once composition has accepted
/// the plugin, so one that is refused there is a fault row and not also a working one.
/// </para>
/// </remarks>
public sealed record PluginModule(string PluginID, IModule Module, LoadedPlugin Loaded);

/// <summary>What one launch made of the installed plugins.</summary>
/// <param name="Modules">The modules to compose, in the order they loaded.</param>
/// <param name="Catalog">What loaded, what did not, and what is waiting for a restart.</param>
public sealed record PluginLoadResult(IReadOnlyList<PluginModule> Modules, PluginCatalog Catalog);

/// <summary>
/// Brings installed plugins into this composition.
/// </summary>
/// <remarks>
/// It runs during host construction, because a module's services must be registered while the
/// service collection is still open. That is also why installing takes effect at the next launch
/// rather than immediately, and why a removal happens here: this is the one moment the files are
/// not yet open.
/// <para>
/// Nothing here throws. A plugin that cannot be loaded becomes a row with a reason on it and the
/// application starts without it — including one that throws from its own code, because every call
/// into a plugin is made inside a filter that turns the exception into that row. What a plugin says
/// about itself is asked once and kept: see <see cref="GuardedPluginModule"/>.
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
    /// <param name="reservedModuleNames">
    /// The module names this build already answers to. Attachment is keyed by name, so a plugin
    /// taking one would own the toggle for somebody else's module.
    /// </param>
    public static PluginLoadResult LoadAll(
        PluginPaths paths,
        PluginXamlHost xaml,
        IReadOnlyCollection<string> reservedPageKeys,
        IReadOnlyCollection<string> reservedModuleNames)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(xaml);
        ArgumentNullException.ThrowIfNull(reservedPageKeys);
        ArgumentNullException.ThrowIfNull(reservedModuleNames);

        var catalog = new PluginCatalog();
        var state = PluginState.Load(paths);
        Settle(paths, state, catalog);

        var taken = new HashSet<string>(reservedPageKeys, StringComparer.Ordinal);
        var named = new HashSet<string>(reservedModuleNames, StringComparer.Ordinal);
        var modules = new List<PluginModule>();

        foreach (var record in state.Records)
        {
            // A removal that could not delete keeps its record so the next launch retries, and a
            // plugin the user asked to be rid of must not be loaded in the meantime.
            if (record.PendingRemove || record.InstalledVersion.Length == 0)
            {
                continue;
            }
            var loaded = Load(paths, xaml, record, taken, named, out var module);

            if (loaded.IsFailure)
            {
                catalog.AddFault(record.PluginID, record.InstalledVersion, loaded.Error);

                continue;
            }
            modules.Add(new PluginModule(record.PluginID, module!, new LoadedPlugin(record, loaded.Value)));
        }

        return new PluginLoadResult(modules, catalog);
    }

    /// <summary>
    /// The page keys a build owns before any plugin is loaded.
    /// </summary>
    /// <param name="items">Every pane destination, whether a module's or the host's.</param>
    /// <param name="pages">
    /// The screens registered without a pane item of their own. They answer to a key like any
    /// other, so leaving them out would let a plugin claim one.
    /// </param>
    /// <remarks>
    /// The type name without its "Page" suffix, matched the way the navigation service matches it:
    /// a key derived by a different rule would not be the one that collides.
    /// </remarks>
    public static IReadOnlyCollection<string> PageKeysOf(
        IEnumerable<NavigationItem> items, IEnumerable<Type> pages)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(pages);

        return [.. items
            .Select(item => item.PageType)
            .Concat(pages)
            .Select(KeyOf)
            .Where(key => key.Length > 0)];
    }

    /// <summary>
    /// Removes what an uninstall marked, and promotes what an install staged.
    /// </summary>
    /// <remarks>
    /// Both happen before a single assembly is loaded, which is the only moment the files are not
    /// held open. A promotion that fails leaves the staged copy where the next launch finds it, and
    /// one that moved without being recorded is adopted from the disk on the next.
    /// </remarks>
    private static void Settle(PluginPaths paths, PluginState state, PluginCatalog catalog)
    {
        var changed = false;

        foreach (var record in state.Records)
        {
            if (record.PendingRemove)
            {
                var removed = Delete(paths.InstalledRoot(record.PluginID))
                    & Delete(paths.StagedRoot(record.PluginID));

                // The record goes only once the files have. One still held open keeps its record,
                // so the next launch tries again rather than orphaning the directory forever.
                if (removed)
                {
                    state.Remove(record.PluginID);
                    changed = true;
                }

                continue;
            }

            if (record.StagedVersion.Length == 0)
            {
                continue;
            }
            var staged = paths.StagedDirectory(record.PluginID, record.StagedVersion);
            var installed = paths.InstalledDirectory(record.PluginID, record.StagedVersion);

            var promoted = Promote(paths, record, staged, installed);

            if (promoted.IsFailure)
            {
                catalog.AddFault(record.PluginID, record.StagedVersion, promoted.Error);

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

    /// <summary>
    /// Puts a staged version where the loader reads from, and says what happened.
    /// </summary>
    /// <remarks>
    /// Only the version being replaced comes out before the move, and the rest of the plugin's old
    /// versions after it succeeds: a move that fails then leaves the working install where it was,
    /// rather than deleting it and reporting that nothing is installed.
    /// </remarks>
    private static Result Promote(PluginPaths paths, PluginRecord record, string staged, string installed)
    {
        if (!Directory.Exists(staged))
        {
            // The move happened and the record was never written — the one window a crash can land
            // in. What is on disk is the answer, so adopt it rather than faulting forever.
            return Directory.Exists(installed)
                ? Result.Success()
                : Result.Failure(PluginLoadErrors.DirectoryMissing(staged));
        }

        try
        {
            _ = Delete(installed);
            var parent = Path.GetDirectoryName(installed);

            if (parent is not null)
            {
                Directory.CreateDirectory(parent);
            }
            Directory.Move(staged, installed);
            Sweep(paths.InstalledRoot(record.PluginID), installed);
            _ = Delete(paths.StagedRoot(record.PluginID));

            return Result.Success();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Result.Failure(PluginLoadErrors.PromoteFailed(record.StagedVersion, exception.Message));
        }
    }

    private static Result<PluginManifest> Load(
        PluginPaths paths,
        PluginXamlHost xaml,
        PluginRecord record,
        HashSet<string> taken,
        HashSet<string> named,
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
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or JsonException)
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

        // Before anything is opened. Attachment is keyed by module name, so a plugin taking one
        // this build already answers to would hide that module's row and own its toggle.
        if (!named.Add(manifest.ModuleName))
        {
            return Result.Failure<PluginManifest>(
                PluginLoadErrors.ModuleNameTaken(manifest.ModuleName));
        }
        var library = Path.Combine(directory, PluginPackage.LibraryFolder.TrimEnd('/'));
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
        var described = Describe(created.Value);

        if (described.IsFailure)
        {
            return Result.Failure<PluginManifest>(described.Error);
        }

        // Last, because neither seam has a removal: a plugin seated here and then refused would
        // answer resource lookups and claim XAML type names for the life of the process.
        foreach (var priFile in manifest.PriFiles)
        {
            var added = xaml.AddPackage(Path.Combine(library, priFile));

            if (added.IsFailure)
            {
                return Result.Failure<PluginManifest>(added.Error);
            }
        }
        var registered = xaml.AddMetadataProvider(assembly);

        if (registered.IsFailure)
        {
            return Result.Failure<PluginManifest>(registered.Error);
        }

        // From here nothing asks the plugin again: what it said is what every reader is given.
        module = new GuardedPluginModule(
            created.Value, manifest.ModuleName, described.Value, reserved.Value);

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
    /// <remarks>
    /// The plugin's own code answers what those pages are, so the call is made inside a filter: a
    /// module that throws from it is a row with a reason rather than an application that will not
    /// start.
    /// </remarks>
    private static Result<IReadOnlyList<NavigationItem>> ClaimPageKeys(IModule module, HashSet<string> taken)
    {
        IReadOnlyList<NavigationItem> items;

        try
        {
            items = [.. module.GetNavigationItems()];
        }
        catch (Exception exception)
        {
            return Result.Failure<IReadOnlyList<NavigationItem>>(
                PluginLoadErrors.Threw(nameof(IModule.GetNavigationItems), exception));
        }
        var keys = new List<string>();

        foreach (var item in items)
        {
            var key = KeyOf(item.PageType);

            if (key.Length == 0)
            {
                return Result.Failure<IReadOnlyList<NavigationItem>>(PluginLoadErrors.Invalid(
                    $"'{item.PageType.Name}' is not a page name the navigation service can key."));
            }

            if (taken.Contains(key))
            {
                return Result.Failure<IReadOnlyList<NavigationItem>>(PluginLoadErrors.PageKeyTaken(key));
            }
            keys.Add(key);
        }

        foreach (var key in keys)
        {
            _ = taken.Add(key);
        }

        return Result.Success(items);
    }

    /// <summary>The plugin's own account of itself, asked once and inside a filter.</summary>
    private static Result<ModuleDescriptor> Describe(IModule module)
    {
        try
        {
            return Result.Success(module.Descriptor);
        }
        catch (Exception exception)
        {
            return Result.Failure<ModuleDescriptor>(
                PluginLoadErrors.Threw(nameof(IModule.Descriptor), exception));
        }
    }

    private static string KeyOf(Type pageType)
    {
        const string suffix = "Page";
        var name = pageType.Name;

        return name.Length > suffix.Length && name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? name[..^suffix.Length]
            : string.Empty;
    }

    /// <summary>Removes every version of a plugin except the one now in use.</summary>
    private static void Sweep(string installedRoot, string keep)
    {
        if (!Directory.Exists(installedRoot))
        {
            return;
        }

        foreach (var directory in Directory.EnumerateDirectories(installedRoot))
        {
            if (!directory.Equals(keep, StringComparison.OrdinalIgnoreCase))
            {
                _ = Delete(directory);
            }
        }
    }

    /// <summary>Removes a directory if it is there, and says whether it is gone.</summary>
    /// <remarks>
    /// Never throws: failing here would strand the application on a plugin it is trying to be rid
    /// of. The answer is what lets the caller keep the record and try again next launch.
    /// </remarks>
    private static bool Delete(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }

            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using __ROOT_NAMESPACE__.SharedKernel;

namespace __ROOT_NAMESPACE__.PluginSdk.Abstractions;

/// <summary>
/// What can only be decided by opening the assemblies a manifest names.
/// </summary>
/// <remarks>
/// The other half of <see cref="PluginManifestValidator"/>, and separate because these checks cost
/// a metadata read of every assembly in the package while that one costs nothing. Both accumulate
/// rather than stopping at the first problem, for the same reason.
/// <para>
/// Everything here is a name comparison over metadata. Nothing is executed, which is what a
/// packaging tool has to promise: it is asked to look at code nobody has agreed to run.
/// </para>
/// </remarks>
public static class PluginContentValidator
{
    private const string _assemblyExtension = ".dll";
    private const string _markupExtension = ".xaml";
    private const string _localizedAttribute = "x:Uid";

    /// <summary>Every reason the assemblies in a package disagree with its manifest.</summary>
    public static IReadOnlyList<Error> Validate(PluginPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        var manifest = package.Manifest;
        var errors = new List<Error>();
        var pages = new List<string>();

        foreach (var file in package.LibraryFiles.Where(IsAssembly))
        {
            var isEntry = file.Equals(manifest.EntryAssembly, StringComparison.OrdinalIgnoreCase);

            using var content = package.ReadLibraryFile(file);

            if (content is null)
            {
                continue;
            }
            var read = PluginAssembly.Read(content);

            if (read.IsFailure)
            {
                // A build drags in native libraries an author never chose, so only the one the
                // manifest points at has to be readable.
                if (isEntry)
                {
                    errors.Add(PluginErrors.EntryAssemblyUnreadable(file));
                }

                continue;
            }
            var assembly = read.Value;
            pages.AddRange(assembly.PageTypes);

            // Declared rather than merely present: the host loads the indexes the manifest names,
            // so one that is packed and unnamed is one nothing ever opens.
            if (assembly.DeclaresXaml && !DeclaresIndexFor(manifest, file))
            {
                errors.Add(PluginErrors.ResourceIndexUndeclared(file));
            }

            if (isEntry)
            {
                AddModuleErrors(assembly, manifest.ModuleType, errors);
            }
        }
        AddPageErrors(pages, manifest, errors);

        return errors;
    }

    /// <summary>
    /// The one check that can only run against a project, because a packed plugin holds no markup.
    /// </summary>
    /// <remarks>
    /// The XAML is compiled into the resource index, so by the time there is a package there is
    /// nothing left to read. An author finds out here or in front of a user.
    /// </remarks>
    public static IReadOnlyList<Error> ValidateMarkup(string projectDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(projectDirectory);
        var errors = new List<Error>();

        try
        {
            var markup = Directory.EnumerateFiles(
                projectDirectory, "*" + _markupExtension, SearchOption.AllDirectories);

            foreach (var file in markup.Where(Localized))
            {
                errors.Add(PluginErrors.LocalizedMarkup(Path.GetRelativePath(projectDirectory, file)));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            errors.Add(PluginErrors.OutputUnwritable(exception.Message));
        }

        return errors;
    }

    private static bool IsAssembly(string file)
    {
        return file.EndsWith(_assemblyExtension, StringComparison.OrdinalIgnoreCase);
    }

    private static bool Localized(string file)
    {
        return File
            .ReadAllText(file)
            .Contains(_localizedAttribute, StringComparison.Ordinal);
    }

    /// <summary>Whether the manifest names a resource index belonging to this assembly.</summary>
    private static bool DeclaresIndexFor(PluginManifest manifest, string assembly)
    {
        var stem = Path.GetFileNameWithoutExtension(assembly);

        return manifest.PriFiles.Any(pri => Path
            .GetFileNameWithoutExtension(pri)
            .Equals(stem, StringComparison.OrdinalIgnoreCase));
    }

    private static void AddModuleErrors(PluginAssembly assembly, string moduleType, List<Error> errors)
    {
        if (!assembly.Declares(moduleType))
        {
            errors.Add(PluginErrors.ModuleTypeMissing(moduleType));

            return;
        }

        if (!assembly.DeclaresModule(moduleType))
        {
            errors.Add(PluginErrors.ModuleTypeNotAModule(moduleType));
        }
    }

    /// <summary>
    /// What the pages in the package say, against what the manifest says about them.
    /// </summary>
    /// <remarks>
    /// One direction only. A plugin may hold pages reached from inside its own screens rather than
    /// from the pane, so a page the manifest does not mention is not a mistake — while a manifest
    /// naming a screen the package cannot show is one the install dialog would have promised.
    /// </remarks>
    private static void AddPageErrors(
        IReadOnlyList<string> pages, PluginManifest manifest, List<Error> errors)
    {
        var names = pages
            .Select(SimpleName)
            .ToHashSet(StringComparer.Ordinal);

        var misnamed = names.Where(
            name => !name.EndsWith(PluginManifestValidator.PageSuffix, StringComparison.Ordinal));

        foreach (var page in misnamed)
        {
            errors.Add(PluginErrors.PageNameInvalid(page));
        }

        foreach (var entry in manifest.Navigation.Where(entry => !names.Contains(entry.Page)))
        {
            errors.Add(PluginErrors.NavigationPageMissing(entry.Page));
        }
    }

    private static string SimpleName(string typeName)
    {
        var dot = typeName.LastIndexOf('.');

        return dot < 0 ? typeName : typeName[(dot + 1)..];
    }
}

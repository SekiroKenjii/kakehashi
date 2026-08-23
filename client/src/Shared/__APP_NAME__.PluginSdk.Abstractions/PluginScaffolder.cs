using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using __ROOT_NAMESPACE__.SharedKernel;

namespace __ROOT_NAMESPACE__.PluginSdk.Abstractions;

/// <summary>What the Develop tab was asked to write.</summary>
/// <param name="ModuleName">PascalCase, and what every generated name is derived from.</param>
/// <param name="DisplayName">What the pane and the catalog row call it.</param>
/// <param name="Icon">A name from the client's icon vocabulary, never a glyph.</param>
/// <param name="Directory">Where the project goes. Must be empty or absent.</param>
/// <param name="WithSamplePage">Whether to write a page, or only the module that could host one.</param>
public sealed record PluginProjectRequest(
    string ModuleName, string DisplayName, string Icon, string Directory, bool WithSamplePage);

/// <summary>
/// Writes a plugin project that builds and loads without anything being edited first.
/// </summary>
/// <remarks>
/// The templates are embedded rather than fetched, and the generated project references assemblies
/// in a directory the caller names rather than a package feed. Both follow from the same
/// requirement: an application somebody scaffolded is standalone, and a plugin project it writes
/// cannot depend on the generator that produced the application
/// (docs/adr/0021-upgrade-is-a-three-way-merge.md).
/// <para>
/// In the SDK rather than the application, so the packaging tool can write a project too — which is
/// what lets a build server compile one and find out that the templates still produce something
/// that builds.
/// </para>
/// </remarks>
public sealed partial class PluginScaffolder
{
    private const string _templatePrefix = "Templates.";

    private readonly string _hostDirectory;

    public PluginScaffolder(string hostDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(hostDirectory);
        _hostDirectory = hostDirectory;
    }

    /// <summary>Writes the project, and returns the files it wrote.</summary>
    public Result<IReadOnlyList<string>> Create(PluginProjectRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var checkedRequest = Validate(request);

        if (checkedRequest.IsFailure)
        {
            return Result.Failure<IReadOnlyList<string>>(checkedRequest.Error);
        }
        var values = Values(request);
        var written = new List<string>();

        try
        {
            foreach (var (template, relative) in Layout(request))
            {
                var path = Path.Combine(request.Directory, Render(relative, values));
                var parent = Path.GetDirectoryName(path);

                if (parent is not null)
                {
                    System.IO.Directory.CreateDirectory(parent);
                }
                File.WriteAllText(path, Render(Read(template), values));
                written.Add(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Result.Failure<IReadOnlyList<string>>(PluginErrors.ProjectInvalid(exception.Message));
        }

        return Result.Success<IReadOnlyList<string>>(written);
    }

    /// <summary>
    /// The assembly name a module of this name produces, which is what the catalog row shows before
    /// anything has been written.
    /// </summary>
    public static string AssemblyNameFor(string moduleName)
    {
        return $"__APP_NAME__.Modules.{moduleName}.UI";
    }

    /// <summary>The catalog identity derived from a module name: WeatherEditor becomes weather-editor.</summary>
    public static string PluginIDFor(string moduleName)
    {
        return SplitWords()
            .Replace(moduleName, "$1-$2")
            .ToLowerInvariant();
    }

    /// <summary>Why a name would be refused, or none.</summary>
    public static Result CheckModuleName(string moduleName)
    {
        if (!ModuleNamePattern().IsMatch(moduleName))
        {
            return Result.Failure(PluginErrors.ProjectInvalid(
                "A module name is PascalCase letters and digits, starting with a letter."));
        }
        var manifest = new PluginManifest {
            SchemaVersion = PluginManifestValidator.SupportedSchemaVersion,
            Id = PluginIDFor(moduleName),
            ModuleName = moduleName,
            DisplayName = moduleName,
            Version = "0.1.0",
            EntryAssembly = AssemblyNameFor(moduleName) + ".dll",
            ModuleType = $"__ROOT_NAMESPACE__.Modules.{moduleName}.UI.{moduleName}Module",
            MinHostSdk = PluginSdkVersion.Current.ToString(),
        };
        var problems = PluginManifestValidator.Validate(manifest);

        // Checked against the same validator the packaging tool runs, so a name that scaffolds is a
        // name that packs.
        return problems.Count == 0
            ? Result.Success()
            : Result.Failure(PluginErrors.ProjectInvalid(problems[0].Message));
    }

    [GeneratedRegex("^[A-Z][A-Za-z0-9]*$")]
    private static partial Regex ModuleNamePattern();

    [GeneratedRegex(@"^[\p{L}\p{N}][\p{L}\p{N} .'-]*$")]
    private static partial Regex DisplayNamePattern();

    [GeneratedRegex("^[a-z][a-z0-9-]*$")]
    private static partial Regex IconPattern();

    [GeneratedRegex("([a-z0-9])([A-Z])")]
    private static partial Regex SplitWords();

    private static string Read(string template)
    {
        var name = _templatePrefix + template;

        using var stream = typeof(PluginScaffolder).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"The template '{name}' is not embedded.");
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }

    private static string Render(string text, IReadOnlyDictionary<string, string> values)
    {
        foreach (var (key, value) in values)
        {
            text = text.Replace($"{{{{{key}}}}}", value, StringComparison.Ordinal);
        }

        return text;
    }

    private static Result Validate(PluginProjectRequest request)
    {
        var name = CheckModuleName(request.ModuleName);

        if (name.IsFailure)
        {
            return name;
        }

        // Both reach a JSON string, a C# literal and a XAML attribute in the same pass, so a quote
        // or a backslash in either is a project that does not compile. Empty falls back instead.
        if (request.DisplayName.Length > 0 && !DisplayNamePattern().IsMatch(request.DisplayName))
        {
            return Result.Failure(PluginErrors.ProjectInvalid(
                "A display name is letters, digits, spaces and simple punctuation."));
        }

        if (request.Icon.Length > 0 && !IconPattern().IsMatch(request.Icon))
        {
            return Result.Failure(PluginErrors.ProjectInvalid(
                "An icon is a lower-case name from the icon set, like 'document'."));
        }

        if (request.Directory.Length == 0)
        {
            return Result.Failure(PluginErrors.ProjectInvalid("A project needs somewhere to go."));
        }

        // Refused rather than merged into: a half-overwritten project is worse than none, and the
        // person who chose the folder is the only one who knows what is already in it.
        if (System.IO.Directory.Exists(request.Directory))
        {
            var existing = System.IO.Directory.EnumerateFileSystemEntries(request.Directory);

            if (existing.Any())
            {
                return Result.Failure(PluginErrors.ProjectInvalid($"'{request.Directory}' is not empty."));
            }
        }

        return Result.Success();
    }

    /// <summary>The one destination a scaffolded page is reached by, as the manifest spells it.</summary>
    private static string Navigation(PluginProjectRequest request)
    {
        var title = request.DisplayName.Length == 0 ? request.ModuleName : request.DisplayName;
        var icon = request.Icon.Length == 0 ? "document" : request.Icon;

        return $$"""
            [
                {
                  "title": "{{title}}",
                  "icon": "{{icon}}",
                  "group": "Utilities",
                  "page": "{{request.ModuleName}}Page"
                }
              ]
            """;
    }

    private static IEnumerable<(string Template, string Path)> Layout(PluginProjectRequest request)
    {
        yield return ("Manifest.tmpl", "manifest.json");
        yield return ("DirectoryBuildProps.tmpl", "Directory.Build.props");
        yield return ("Readme.tmpl", "README.md");
        yield return ("GitIgnore.tmpl", ".gitignore");
        yield return ("ModuleProject.tmpl", "{{AssemblyName}}/{{AssemblyName}}.csproj");

        // Two entry points rather than one with a hole in it: a template naming a view that was
        // never written is a project that does not compile.
        if (!request.WithSamplePage)
        {
            yield return ("ModuleEntryPointBare.tmpl", "{{AssemblyName}}/{{Module}}Module.cs");

            yield break;
        }
        yield return ("ModuleEntryPoint.tmpl", "{{AssemblyName}}/{{Module}}Module.cs");
        yield return ("PageMarkup.tmpl", "{{AssemblyName}}/Views/{{Module}}Page.xaml");
        yield return ("PageCodeBehind.tmpl", "{{AssemblyName}}/Views/{{Module}}Page.xaml.cs");
        yield return ("PageViewModel.tmpl", "{{AssemblyName}}/ViewModels/{{Module}}PageViewModel.cs");
    }

    /// <summary>What every template's placeholders are replaced with.</summary>
    /// <remarks>
    /// PriFiles and Navigation are rendered rather than literal because both are empty without a
    /// page: a resource index exists only where there is compiled XAML, and a screen the manifest
    /// promises is one the package has to be able to show.
    /// </remarks>
    private Dictionary<string, string> Values(PluginProjectRequest request)
    {
        return new Dictionary<string, string>(StringComparer.Ordinal) {
            ["Module"] = request.ModuleName,
            ["DisplayName"] = request.DisplayName.Length == 0 ? request.ModuleName : request.DisplayName,
            ["PluginId"] = PluginIDFor(request.ModuleName),
            ["AssemblyName"] = AssemblyNameFor(request.ModuleName),
            ["RootNamespace"] = $"__ROOT_NAMESPACE__.Modules.{request.ModuleName}.UI",
            ["HostSdk"] = PluginSdkVersion.Current.ToString(),
            ["HostDirectory"] = _hostDirectory,
            ["PriFiles"] = request.WithSamplePage ? $"[\"{AssemblyNameFor(request.ModuleName)}.pri\"]" : "[]",
            ["Navigation"] = request.WithSamplePage ? Navigation(request) : "[]",
        };
    }
}

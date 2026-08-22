using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using __ROOT_NAMESPACE__.PluginSdk.Abstractions;
using __ROOT_NAMESPACE__.SharedKernel;

namespace __ROOT_NAMESPACE__.App.Plugins;

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
/// The templates are embedded in this application rather than fetched, and the generated project
/// references the assemblies sitting beside this executable rather than a package feed. Both follow
/// from the same requirement: an application somebody scaffolded is standalone, and a plugin project
/// it writes cannot depend on the generator that produced the application
/// (docs/adr/0021-upgrade-is-a-three-way-merge.md).
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
            return Result.Failure<IReadOnlyList<string>>(PluginLoadErrors.Invalid(exception.Message));
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
            return Result.Failure(PluginLoadErrors.Invalid(
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
            : Result.Failure(PluginLoadErrors.Invalid(problems[0].Message));
    }

    [GeneratedRegex("^[A-Z][A-Za-z0-9]*$")]
    private static partial Regex ModuleNamePattern();

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

        if (request.Directory.Length == 0)
        {
            return Result.Failure(PluginLoadErrors.Invalid("A project needs somewhere to go."));
        }

        // Refused rather than merged into: a half-overwritten project is worse than none, and the
        // person who chose the folder is the only one who knows what is already in it.
        if (System.IO.Directory.Exists(request.Directory))
        {
            var existing = System.IO.Directory.EnumerateFileSystemEntries(request.Directory);

            if (existing.Any())
            {
                return Result.Failure(PluginLoadErrors.Invalid($"'{request.Directory}' is not empty."));
            }
        }

        return Result.Success();
    }

    private static IEnumerable<(string Template, string Path)> Layout(PluginProjectRequest request)
    {
        yield return ("Manifest.tmpl", "manifest.json");
        yield return ("DirectoryBuildProps.tmpl", "Directory.Build.props");
        yield return ("Readme.tmpl", "README.md");
        yield return ("GitIgnore.tmpl", ".gitignore");
        yield return ("ModuleProject.tmpl", "{{AssemblyName}}/{{AssemblyName}}.csproj");
        yield return ("ModuleEntryPoint.tmpl", "{{AssemblyName}}/{{Module}}Module.cs");

        if (request.WithSamplePage)
        {
            yield return ("PageMarkup.tmpl", "{{AssemblyName}}/Views/{{Module}}Page.xaml");
            yield return ("PageCodeBehind.tmpl", "{{AssemblyName}}/Views/{{Module}}Page.xaml.cs");
            yield return ("PageViewModel.tmpl", "{{AssemblyName}}/ViewModels/{{Module}}PageViewModel.cs");
        }
    }

    private Dictionary<string, string> Values(PluginProjectRequest request)
    {
        return new Dictionary<string, string>(StringComparer.Ordinal) {
            ["Module"] = request.ModuleName,
            ["DisplayName"] = request.DisplayName.Length == 0 ? request.ModuleName : request.DisplayName,
            ["Icon"] = request.Icon.Length == 0 ? "document" : request.Icon,
            ["PluginId"] = PluginIDFor(request.ModuleName),
            ["AssemblyName"] = AssemblyNameFor(request.ModuleName),
            ["RootNamespace"] = $"__ROOT_NAMESPACE__.Modules.{request.ModuleName}.UI",
            ["HostSdk"] = PluginSdkVersion.Current.ToString(),
            ["HostDirectory"] = _hostDirectory,
            ["Year"] = DateTime.UtcNow.Year.ToString(CultureInfo.InvariantCulture),
        };
    }
}

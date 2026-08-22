using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using __ROOT_NAMESPACE__.App.Plugins;
using __ROOT_NAMESPACE__.PluginSdk.Abstractions;
using __ROOT_NAMESPACE__.SharedKernel;

namespace __ROOT_NAMESPACE__.App.UI;

/// <summary>
/// The Develop tab: scaffolding a plugin project that builds and loads without being edited first.
/// </summary>
/// <remarks>
/// Split from the rest of the view model because it shares nothing with it but the page it sits on
/// — the same reason the navigation screen keeps its node editing in a file of its own.
/// </remarks>
public sealed partial class PluginsViewModel
{
    [ObservableProperty]
    private string _newModuleName = string.Empty;

    [ObservableProperty]
    private string _newDisplayName = string.Empty;

    [ObservableProperty]
    private string _newIcon = "document";

    [ObservableProperty]
    private string _newLocation = string.Empty;

    [ObservableProperty]
    private bool _withSamplePage = true;

    [ObservableProperty]
    private string _scaffoldResult = string.Empty;

    [ObservableProperty]
    private string _projectPath = string.Empty;

    [ObservableProperty]
    private string _toolResult = string.Empty;

    /// <summary>What the module name would produce, shown while it is being typed.</summary>
    public string NewProjectPreview
    {
        get {
            if (NewModuleName.Length == 0)
            {
                return string.Empty;
            }
            var checkedName = PluginScaffolder.CheckModuleName(NewModuleName);

            return checkedName.IsFailure
                ? checkedName.Error.Message
                : $"{PluginScaffolder.AssemblyNameFor(NewModuleName)} · id {PluginScaffolder.PluginIDFor(NewModuleName)}";
        }
    }

    public bool IsNewProjectNameValid =>
        NewModuleName.Length > 0 && PluginScaffolder.CheckModuleName(NewModuleName).IsSuccess;

    public bool CanCreateProject => IsNewProjectNameValid && NewLocation.Length > 0;

    /// <summary>The tree the tab draws beside the form, so the shape is known before it is written.</summary>
    public IReadOnlyList<string> GeneratedStructure
    {
        get {
            var assembly = NewModuleName.Length == 0
                ? "<App>.Modules.<Module>.UI"
                : PluginScaffolder.AssemblyNameFor(NewModuleName);
            var module = NewModuleName.Length == 0 ? "<Module>" : NewModuleName;
            var files = new List<string> {
                "manifest.json — id, version, the host it needs",
                "Directory.Build.props — where the host's assemblies are",
                $"{assembly}/",
                $"    {module}Module.cs — the entry point the host activates",
            };

            if (WithSamplePage)
            {
                files.Add($"    Views/{module}Page.xaml");
                files.Add($"    ViewModels/{module}PageViewModel.cs");
            }
            files.Add("README.md");

            return files;
        }
    }

    /// <summary>What to do with the project once it exists.</summary>
    public IReadOnlyList<string> NextSteps { get; } = [
        "Open it in your editor. It references the application's own assemblies, never its internals.",
        "Build it. DisableEmbeddedXbf is already false, which is what puts the XAML where the host looks.",
        "Check and pack it below, then install the file from the Installed tab.",
        "Restart. A plugin's services are registered while the container is still open, which is earlier than any screen exists.",
    ];

    /// <summary>The same two steps on a build server, where there is no window.</summary>
    /// <remarks>
    /// The tool runs the checks this tab runs, out of the same library, so what it reports here is
    /// what it reports there.
    /// </remarks>
    public string CliCommand =>
        $"__APP_NAME_LOWER__-plugin validate {Quoted(ProjectPath)}\n"
            + $"__APP_NAME_LOWER__-plugin pack {Quoted(ProjectPath)}";

    public bool CanCheckProject => ProjectPath.Length > 0;

    public async Task BrowseForProjectAsync()
    {
        if (await _files.PickFolderAsync() is { } folder)
        {
            ProjectPath = folder;
        }
    }

    /// <summary>
    /// Reports everything wrong with a project, or that there is nothing.
    /// </summary>
    /// <remarks>
    /// The project is packed in memory and the package is what gets checked, so this answers the
    /// question an install would ask rather than a question about a directory.
    /// </remarks>
    public void CheckProject()
    {
        ToolResult = Describe(Check(ProjectPath), "Nothing to fix. It is ready to pack.");
    }

    /// <summary>Writes the package, and says where.</summary>
    public void PackProject()
    {
        var packed = PluginPackager.Pack(ProjectPath, ProjectPath);
        ToolResult = packed.IsFailure
            ? packed.Error.Message
            : $"Wrote {packed.Value}";
    }

    public async Task BrowseForLocationAsync()
    {
        if (await _files.PickFolderAsync() is { } folder)
        {
            NewLocation = folder;
        }
    }

    /// <summary>Writes the project, and reports what it wrote.</summary>
    public void CreateProject()
    {
        ScaffoldResult = string.Empty;
        ErrorMessage = string.Empty;

        var written = _scaffolder.Create(new PluginProjectRequest(
            NewModuleName, NewDisplayName, NewIcon, NewLocation, WithSamplePage));

        if (written.IsFailure)
        {
            ErrorMessage = written.Error.Message;
            OnPropertyChanged(nameof(HasError));

            return;
        }
        ScaffoldResult = $"Wrote {written.Value.Count} files to {NewLocation}.";
    }

    partial void OnNewModuleNameChanged(string value)
    {
        OnPropertyChanged(nameof(NewProjectPreview));
        OnPropertyChanged(nameof(IsNewProjectNameValid));
        OnPropertyChanged(nameof(CanCreateProject));
        OnPropertyChanged(nameof(GeneratedStructure));
    }

    partial void OnNewLocationChanged(string value) => OnPropertyChanged(nameof(CanCreateProject));

    partial void OnWithSamplePageChanged(bool value) => OnPropertyChanged(nameof(GeneratedStructure));

    private static string Quoted(string path)
    {
        return path.Length == 0 ? "<project>" : $"\"{path}\"";
    }

    private static string Describe(IReadOnlyList<Error> problems, string whenNone)
    {
        return problems.Count == 0
            ? whenNone
            : string.Join("\n", problems.Select(problem => problem.Message));
    }

    private static IReadOnlyList<Error> Check(string projectDirectory)
    {
        var built = PluginPackager.Build(projectDirectory);

        if (built.IsFailure)
        {
            return [built.Error];
        }

        using var archive = built.Value;
        var opened = PluginPackage.Open(archive, leaveOpen: true);

        if (opened.IsFailure)
        {
            return [opened.Error];
        }

        using var package = opened.Value;

        return [
            .. package.Validate(),
            .. PluginContentValidator.Validate(package),
            .. PluginContentValidator.ValidateMarkup(projectDirectory),
        ];
    }

    partial void OnProjectPathChanged(string value)
    {
        OnPropertyChanged(nameof(CanCheckProject));
        OnPropertyChanged(nameof(CliCommand));
        ToolResult = string.Empty;
    }

    /// <summary>The contract version a project scaffolded now is written against.</summary>
    public static string HostSdkVersion => PluginSdkVersion.Current.ToString();
}

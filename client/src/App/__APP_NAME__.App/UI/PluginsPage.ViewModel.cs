using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using __ROOT_NAMESPACE__.App.Plugins;
using __ROOT_NAMESPACE__.App.Services;
using __ROOT_NAMESPACE__.PluginSdk.Abstractions;
using __ROOT_NAMESPACE__.UI.Common.Controls;
using __ROOT_NAMESPACE__.UI.Contracts;
using __ROOT_NAMESPACE__.UI.Contracts.Services.Platform;

namespace __ROOT_NAMESPACE__.App.UI;

/// <summary>How a module got into this composition, which is what the badge says.</summary>
public enum PluginOrigin
{
    /// <summary>Compiled into the application. Never removable.</summary>
    BuiltIn,

    /// <summary>Installed, and signed by this application's own publisher.</summary>
    Verified,

    /// <summary>Installed, and signed by somebody else or by nobody.</summary>
    Unofficial,

    /// <summary>Installed, and this launch could not load it.</summary>
    Faulted,
}

/// <summary>One row of the installed list.</summary>
public sealed record PluginListItem(
    string ModuleName,
    string PluginID,
    string DisplayName,
    string Version,
    string Description,
    string Meta,
    string Warning,
    PluginOrigin Origin,
    string StagedVersion,
    bool IsEnabled,
    bool CanToggle,
    bool CanUninstall)
{
    /// <summary>What the badge beside the name reads.</summary>
    public string OriginLabel => Origin switch {
        PluginOrigin.BuiltIn => "Built-in",
        PluginOrigin.Verified => "Verified",
        PluginOrigin.Unofficial => "Unofficial",
        _ => "Not loaded",
    };
}

/// <summary>
/// The Plugins screen: what this composition is made of, and what to do about it.
/// </summary>
/// <remarks>
/// It shows compiled-in modules and installed ones in one list on purpose. To somebody deciding
/// whether to turn something off, where it came from is a property of the row rather than a reason
/// to look somewhere else — and where it came from is exactly what the badge and the warning line
/// are for.
/// </remarks>
public sealed partial class PluginsViewModel : ViewModel
{
    /// <summary>
    /// What a row says about a package this application cannot vouch for.
    /// </summary>
    /// <remarks>
    /// Here rather than in the markup because the view model composes the row, and it is worth one
    /// place: a warning that drifts between the list and the install prompt is a warning nobody
    /// believes.
    /// </remarks>
    public const string UnsignedWarning =
        "Unsigned — runs with full application privileges. Installed at your own risk.";

    private const string _allFilter = "All";
    private const string _fileSource = "File";
    private const string _catalogSource = "Catalog";
    private const string _installedTab = "Installed";
    private const string _browseTab = "Browse catalog";
    private const string _developTab = "Develop";

    private readonly IModuleRegistry _modules;
    private readonly PluginCatalog _catalog;
    private readonly PluginInstaller _installer;
    private readonly IFileOpenService _files;
    private readonly IDialogService _dialogs;
    private readonly PluginScaffolder _scaffolder;
    private readonly IPluginCatalogService _catalogService;

    private List<PluginListItem> _all = [];

    /// <summary>Where the package in the prompt came from, which the installation records.</summary>
    private string _pendingSource = _fileSource;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _filter = _allFilter;

    [ObservableProperty]
    private string _tab = _installedTab;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    /// <summary>The package waiting on the consent dialog, if one is open.</summary>
    [ObservableProperty]
    private PluginPreview? _pending;

    [ObservableProperty]
    private bool _consentGiven;

    public PluginsViewModel(
        IModuleRegistry modules,
        PluginCatalog catalog,
        PluginInstaller installer,
        IFileOpenService files,
        IDialogService dialogs,
        PluginScaffolder scaffolder,
        IPluginCatalogService catalogService)
    {
        ArgumentNullException.ThrowIfNull(modules);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(installer);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(scaffolder);
        ArgumentNullException.ThrowIfNull(catalogService);
        _modules = modules;
        _catalog = catalog;
        _installer = installer;
        _files = files;
        _dialogs = dialogs;
        _scaffolder = scaffolder;
        _catalogService = catalogService;
    }

    public ObservableCollection<PluginListItem> Items { get; } = [];

    public ObservableCollection<StatCard> StatCards { get; } = [];

    public IReadOnlyList<string> Filters { get; } =
        [_allFilter, "Built-in", "Verified", "Unofficial", "Disabled"];

    public bool HasError => ErrorMessage.Length > 0;

    /// <summary>Which tab the page is showing. The strip is the only thing that sets it.</summary>
    public bool ShowingInstalled => Tab == _installedTab;

    public bool ShowingBrowse => Tab == _browseTab;

    public bool ShowingDevelop => Tab == _developTab;

    /// <summary>Whether anything is waiting for the application to be restarted.</summary>
    public bool RestartRequired => _catalog.RestartRequired;

    public string RestartMessage
    {
        get {
            var waiting = _catalog.AwaitingRestart;

            if (waiting.Count == 0)
            {
                return string.Empty;
            }

            if (waiting.Count > 1)
            {
                return string.Create(
                    CultureInfo.CurrentCulture,
                    $"{waiting.Count} changes are staged and take effect on the next launch.");
            }
            var record = waiting[0];

            return record.PendingRemove
                ? $"{record.DisplayName} is removed on the next launch."
                : $"{record.DisplayName} {record.StagedVersion} is staged and loads on the next launch.";
        }
    }

    /// <summary>The consent prompt is only shown for what this application cannot vouch for.</summary>
    public bool ConsentRequired => Pending is not null && Pending.Trust.Level != PluginTrustLevel.Verified;

    public bool CanInstallPending => Pending is not null && (!ConsentRequired || ConsentGiven);

    public string PendingName => Pending?.Manifest.DisplayName ?? string.Empty;

    /// <summary>The identity line under the name: what it is, and how big.</summary>
    public string PendingSummary => Pending is null
        ? string.Empty
        : $"{Pending.Manifest.Id} v{Pending.Manifest.Version} · {Size(Pending.SizeInBytes)}";

    public string PendingAuthor => Pending?.Manifest.Author ?? string.Empty;

    public string PendingDigest => Pending?.Trust.SHA256 ?? string.Empty;

    /// <summary>Who signed it, said plainly rather than as a status name.</summary>
    public string PendingSignature => Pending is null
        ? string.Empty
        : Pending.Trust.Level == PluginTrustLevel.Verified
            ? $"Signed by this application's publisher — {Pending.Trust.Signer}"
            : Pending.Trust.Signer.Length == 0
                ? "Unsigned — nobody vouches for this package"
                : $"Signed by somebody else — {Pending.Trust.Signer}";

    /// <summary>The screens it adds, which is the part a user can check against what they expected.</summary>
    public string PendingNavigation
    {
        get {
            if (Pending is null || Pending.Manifest.Navigation.Count == 0)
            {
                return "Nothing — it adds no screen.";
            }

            return string.Join(", ", Pending.Manifest.Navigation.Select(Describe));
        }
    }

    public string PendingHostSdk => Pending is null
        ? string.Empty
        : $"v{Pending.Manifest.MinHostSdk} or later — this build is v{PluginSdkVersion.Current}";

    [RelayCommand]
    public void Load()
    {
        _all = [.. BuiltIns(), .. Installed(), .. Faults()];
        Apply();
        RebuildStats();
        OnPropertyChanged(nameof(RestartRequired));
        OnPropertyChanged(nameof(RestartMessage));
    }

    /// <summary>
    /// Opens a package and holds it while the user decides. The dialog shows what
    /// <see cref="PluginInstaller.Inspect"/> found.
    /// </summary>
    public async Task<bool> PrepareInstallFromFileAsync()
    {
        ErrorMessage = string.Empty;
        OnPropertyChanged(nameof(HasError));
        var path = await _files.PickFileAsync("Plugin package", PluginPaths.PackageExtension);

        return path is not null && Prepare(path, _fileSource);
    }

    /// <summary>Accepts the package the dialog is showing. False keeps the dialog open.</summary>
    /// <remarks>
    /// A catalog install is reported to the deployment that offered it, which is what puts it in
    /// the account's history. The report happens after the package is staged and cannot undo it:
    /// the install is a fact on this machine whether or not the server heard about it.
    /// </remarks>
    public async Task<bool> ConfirmInstallAsync()
    {
        if (Pending is null)
        {
            return false;
        }
        var manifest = Pending.Manifest;
        var committed = _installer.Commit(Pending, ConsentGiven, _pendingSource);

        if (committed.IsFailure)
        {
            return Refuse(committed.Error.Message);
        }
        _catalog.AddAwaitingRestart(new PluginRecord {
            PluginID = manifest.Id,
            DisplayName = manifest.DisplayName,
            StagedVersion = manifest.Version,
        });
        Pending = null;
        Load();

        if (_pendingSource == _catalogSource)
        {
            var reported = await _catalogService.ReportInstalledAsync(
                manifest.Id, manifest.Version, CancellationToken.None);

            if (reported.IsFailure)
            {
                ErrorMessage = $"{manifest.DisplayName} {manifest.Version} is staged and loads on "
                    + $"the next launch. The catalog was not told: {reported.Error.Message}";
                OnPropertyChanged(nameof(HasError));
            }
        }

        return true;
    }

    /// <summary>Throws away a package the user decided against.</summary>
    public void CancelInstall()
    {
        if (Pending is not null)
        {
            _installer.Discard(Pending);
            Pending = null;
        }
    }

    /// <summary>Turns a module on or off. Instant: nothing is loaded or unloaded by it.</summary>
    public void Toggle(PluginListItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var result = item.IsEnabled ? _modules.Detach(item.ModuleName) : _modules.Attach(item.ModuleName);
        ErrorMessage = result.IsFailure ? result.Error.Message : string.Empty;
        OnPropertyChanged(nameof(HasError));
        Load();
    }

    /// <summary>Marks a plugin for removal, which happens at the next launch.</summary>
    public async Task UninstallAsync(PluginListItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var confirmed = await _dialogs.ShowConfirmAsync(
            $"Remove {item.DisplayName}?",
            "It keeps working until the application is restarted, and is deleted when it starts again.",
            "Remove",
            "Cancel");

        if (!confirmed)
        {
            return;
        }
        var result = _installer.Uninstall(item.PluginID);

        if (result.IsFailure)
        {
            ErrorMessage = result.Error.Message;
            OnPropertyChanged(nameof(HasError));

            return;
        }
        _catalog.AddAwaitingRestart(new PluginRecord {
            PluginID = item.PluginID,
            DisplayName = item.DisplayName,
            PendingRemove = true,
        });
        Load();
    }

    partial void OnSearchTextChanged(string value) => Apply();

    partial void OnFilterChanged(string value) => Apply();

    partial void OnTabChanged(string value)
    {
        OnPropertyChanged(nameof(ShowingInstalled));
        OnPropertyChanged(nameof(ShowingBrowse));
        OnPropertyChanged(nameof(ShowingDevelop));
    }

    partial void OnConsentGivenChanged(bool value) => OnPropertyChanged(nameof(CanInstallPending));

    partial void OnPendingChanged(PluginPreview? value)
    {
        foreach (var name in new[] {
            nameof(PendingName), nameof(PendingSummary), nameof(PendingAuthor), nameof(PendingDigest),
            nameof(PendingSignature), nameof(PendingNavigation), nameof(PendingHostSdk),
            nameof(ConsentRequired), nameof(CanInstallPending),
        })
        {
            OnPropertyChanged(name);
        }
    }

    /// <summary>Opens a package and holds it for the prompt, however it arrived.</summary>
    private bool Prepare(string packagePath, string source)
    {
        var inspected = _installer.Inspect(packagePath);

        if (inspected.IsFailure)
        {
            return Refuse(inspected.Error.Message);
        }
        _pendingSource = source;
        ConsentGiven = false;
        Pending = inspected.Value;

        return true;
    }

    /// <summary>Puts a reason on the page, and answers no.</summary>
    private bool Refuse(string message)
    {
        ErrorMessage = message;
        OnPropertyChanged(nameof(HasError));

        return false;
    }

    private static string Describe(PluginNavigationEntry entry)
    {
        return entry.Group.Length == 0
            ? $"\"{entry.Title}\""
            : $"\"{entry.Title}\" under {entry.Group}";
    }

    private static string Size(long bytes)
    {
        return bytes <= 0
            ? string.Empty
            : string.Create(CultureInfo.CurrentCulture, $"{bytes / 1024d / 1024d:0.0} MB");
    }

    private IEnumerable<PluginListItem> BuiltIns()
    {
        var installed = _catalog
            .ModuleNames()
            .ToHashSet(StringComparer.Ordinal);

        foreach (var module in _modules.All.Where(module => !installed.Contains(module.Name)))
        {
            yield return new PluginListItem(
                module.Name,
                string.Empty,
                module.Descriptor.DisplayName,
                string.Empty,
                module.Descriptor.Description,
                $"{module.Name} · part of host build",
                string.Empty,
                PluginOrigin.BuiltIn,
                string.Empty,
                _modules.IsAttached(module.Name),
                !module.Descriptor.IsRequired,
                CanUninstall: false);
        }
    }

    private IEnumerable<PluginListItem> Installed()
    {
        foreach (var plugin in _catalog.Loaded)
        {
            var record = plugin.Record;
            var manifest = plugin.Manifest;
            var verified = record.Signature == nameof(PluginTrustLevel.Verified);
            var parts = new[] {
                manifest.EntryAssembly,
                Size(record.SizeInBytes),
                record.InstalledOn == default
                    ? string.Empty
                    : $"installed {record.InstalledOn.LocalDateTime:yyyy-MM-dd}",
                record.Source.Length == 0 ? string.Empty : record.Source.ToLowerInvariant(),
            };

            yield return new PluginListItem(
                manifest.ModuleName,
                record.PluginID,
                manifest.DisplayName,
                $"v{record.InstalledVersion}",
                manifest.Description,
                string.Join(" · ", parts.Where(part => part.Length > 0)),
                verified ? string.Empty : UnsignedWarning,
                verified ? PluginOrigin.Verified : PluginOrigin.Unofficial,
                record.StagedVersion,
                _modules.IsAttached(manifest.ModuleName),
                CanToggle: true,
                CanUninstall: true);
        }
    }

    private IEnumerable<PluginListItem> Faults()
    {
        foreach (var fault in _catalog.Faults)
        {
            yield return new PluginListItem(
                fault.PluginID,
                fault.PluginID,
                fault.PluginID,
                fault.Version.Length == 0 ? string.Empty : $"v{fault.Version}",
                "This plugin did not load.",
                fault.PluginID,
                fault.Reason.Message,
                PluginOrigin.Faulted,
                string.Empty,
                IsEnabled: false,
                CanToggle: false,
                CanUninstall: true);
        }
    }

    private void Apply()
    {
        var search = SearchText.Trim();
        Items.Clear();

        var shown = _all
            .Where(Matches)
            .Where(item => Named(item, search));

        foreach (var item in shown)
        {
            Items.Add(item);
        }
    }

    private bool Matches(PluginListItem item)
    {
        return Filter switch {
            "Built-in" => item.Origin == PluginOrigin.BuiltIn,
            "Verified" => item.Origin == PluginOrigin.Verified,
            "Unofficial" => item.Origin == PluginOrigin.Unofficial,
            "Disabled" => !item.IsEnabled,
            _ => true,
        };
    }

    private static bool Named(PluginListItem item, string search)
    {
        return search.Length == 0
            || item.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase)
            || item.ModuleName.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private void RebuildStats()
    {
        var builtIn = _all.Count(item => item.Origin == PluginOrigin.BuiltIn);
        var plugins = _all.Count - builtIn;
        var unofficial = _all.Count(item => item.Origin == PluginOrigin.Unofficial);
        var staged = _catalog.AwaitingRestart.Count;

        StatCards.Clear();
        StatCards.Add(new StatCard(
            "Modules",
            _all.Count.ToString(CultureInfo.CurrentCulture),
            string.Create(CultureInfo.CurrentCulture, $"{builtIn} built-in · {plugins} installed"),
            SegoeFluentIcons.Glyph("Puzzle"),
            StatKind.Accent));
        StatCards.Add(new StatCard(
            "Waiting",
            staged.ToString(CultureInfo.CurrentCulture),
            "staged for restart",
            SegoeFluentIcons.Glyph("Download"),
            staged == 0 ? StatKind.Muted : StatKind.Accent));
        StatCards.Add(new StatCard(
            "Unofficial",
            unofficial.ToString(CultureInfo.CurrentCulture),
            "runs unreviewed code",
            SegoeFluentIcons.Glyph("Warning"),
            unofficial == 0 ? StatKind.Muted : StatKind.Warning));
        StatCards.Add(new StatCard(
            "Host SDK",
            $"v{PluginSdkVersion.Current}",
            "plugin contract version",
            SegoeFluentIcons.Glyph("Shield"),
            StatKind.Positive));
    }
}

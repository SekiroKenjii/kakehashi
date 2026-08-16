using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using __ROOT_NAMESPACE__.App.Infrastructure.Backend;
using __ROOT_NAMESPACE__.App.Infrastructure.Backend.Contracts;
using __ROOT_NAMESPACE__.App.Services;
using __ROOT_NAMESPACE__.Application.Abstractions.Messaging;
using __ROOT_NAMESPACE__.Modules.Auth.Application.Sessions.Queries.GetCurrentSession;
using __ROOT_NAMESPACE__.Modules.Auth.UI.Views;
using __ROOT_NAMESPACE__.UI.Contracts;
using __ROOT_NAMESPACE__.UI.Contracts.Services.Platform;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.UI.Xaml;

namespace __ROOT_NAMESPACE__.App.UI;

/// <param name="Command">
/// The shell command that does the step, when one does. It is offered to the clipboard rather than
/// run: this is a developer's terminal, not the app's.
/// </param>
public sealed record GettingStartedStep(
    string Id, string Title, string Subtitle, bool IsDone, bool HasAction, string Command = "")
{
    public bool IsNotDone => !IsDone;

    public bool HasCommand => Command.Length > 0;

    public bool ShowsChevron => HasAction && !IsDone && !HasCommand;
}

/// <summary>
/// One of the three architecture gates, as the Home page lists them: what it protects, and the
/// command that runs it.
/// </summary>
public sealed record GateItem(string Name, string Protects, string Command);

/// <param name="IsWithheld">
/// An administrator has not assigned this module to the account. The tile is drawn locked rather
/// than hidden, and the lock is a courtesy — the server refuses the module's requests regardless.
/// </param>
/// <param name="IsGranted">
/// An administrator assigned this module. It cannot be detached: the grant is not the user's to
/// give back.
/// </param>
public sealed record ModuleCardItem(
    string PageKey,
    string Name,
    string IconGlyph,
    string Badge,
    string Description,
    string FootText,
    string ModuleName,
    bool CanDetach,
    bool IsWithheld = false,
    bool IsGranted = false)
{
    /// <summary>Whether the tile opens its page. A withheld module has nothing to open.</summary>
    public bool CanOpen => !IsWithheld;
}

/// <param name="IsWithheld">
/// Listed but not offerable: the account is not assigned it. Shown so the user learns why the
/// module is missing.
/// </param>
public sealed record DetachedModuleListItem(
    string Name, string DisplayName, string Description, bool IsWithheld = false)
{
    public bool CanAttach => !IsWithheld;
}

public sealed record HomeActivityItem(
    string Title, string Subtitle, string TimeText, string Glyph, bool IsPositive, bool IsAlert)
{
    public bool IsNeutral => !IsPositive && !IsAlert;
}

public sealed partial class HomeViewModel : ViewModel
{
    private const string _dismissedKey = "Home.GettingStartedDismissed";
    private const string _stepDoneKeyPrefix = "Home.StepDone.";
    private const string _backendStepId = "backend";
    private const string _architectureStepId = "architecture";
    private const string _addModuleStepId = "addmodule";
    private const string _modulePrefix = "module:";
    private const string _removePrefix = "remove:";
    /// <summary>
    /// How many modules ship with the template: the example, Activity and Auth. A higher count
    /// means the developer added their own. Keep it equal to the shipped modules, or "add your
    /// first module" reports itself complete on a fresh install.
    /// </summary>
    private const int _shippedModuleCount = 3;
    private const int _pageSize = 5;

    /// <summary>
    /// What the Backend card offers when nothing answers. The whole stack is one compose file, so
    /// this is the command rather than a page of instructions.
    /// </summary>
    private const string _startBackendCommand = "docker compose up -d";

    private readonly ISender _sender;
    private readonly INavigationService _navigationService;
    private readonly IThemeService _themeService;
    private readonly ILocalSettingsService _localSettings;
    private readonly IClipboardService _clipboard;
    private readonly INotificationService _notifications;
    private readonly IBackendClient _backendClient;
    private readonly BackendOptions _backendOptions;
    private readonly IModuleRegistry _moduleRegistry;
    private readonly IReadOnlyList<IGettingStartedStep> _moduleSteps;
    private readonly AppActivityLog _activityLog;
    private readonly bool _isBackendConfigured;
    private List<HomeActivityItem> _allActivity = [];
    private int _activityPage = 1;
    private ModuleCardItem? _pendingDetach;

    [ObservableProperty]
    public partial bool IsAuthenticated { get; set; }

    [ObservableProperty]
    public partial string GreetingText { get; set; }

    [ObservableProperty]
    public partial string DateText { get; set; }

    [ObservableProperty]
    public partial string? AvatarName { get; set; }

    [ObservableProperty]
    public partial bool IsGettingStartedVisible { get; set; }

    [ObservableProperty]
    public partial double StepsProgress { get; set; }

    /// <summary>
    /// How many rows the checklist has. It varies with the project — a bare scaffold has fewer —
    /// so the progress bar takes its maximum from here rather than from a number in the markup.
    /// </summary>
    [ObservableProperty]
    public partial double StepsTotal { get; set; }

    [ObservableProperty]
    public partial string StepsProgressText { get; set; }

    [ObservableProperty]
    public partial string ModulesHeader { get; set; }

    [ObservableProperty]
    public partial bool IsBackendNeutral { get; set; }

    [ObservableProperty]
    public partial bool IsBackendConnected { get; set; }

    [ObservableProperty]
    public partial bool IsBackendOffline { get; set; }

    [ObservableProperty]
    public partial string BackendStatusText { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoActivity))]
    public partial bool HasActivity { get; set; }

    [ObservableProperty]
    public partial bool HasActivityPaging { get; set; }

    [ObservableProperty]
    public partial string ActivityPageLabel { get; set; }

    [ObservableProperty]
    public partial string DetachPrompt { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoDetachedModules))]
    public partial bool HasDetachedModules { get; set; }

    public HomeViewModel(
        ISender sender,
        INavigationService navigationService,
        IThemeService themeService,
        ILocalSettingsService localSettings,
        IClipboardService clipboard,
        INotificationService notifications,
        IBackendClient backendClient,
        IOptions<BackendOptions> backendOptions,
        IConfiguration configuration,
        AppActivityLog activityLog,
        IModuleRegistry moduleRegistry,
        IEnumerable<IGettingStartedStep> moduleSteps)
    {
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(navigationService);
        ArgumentNullException.ThrowIfNull(themeService);
        ArgumentNullException.ThrowIfNull(localSettings);
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(notifications);
        ArgumentNullException.ThrowIfNull(backendClient);
        ArgumentNullException.ThrowIfNull(backendOptions);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(activityLog);
        ArgumentNullException.ThrowIfNull(moduleRegistry);
        ArgumentNullException.ThrowIfNull(moduleSteps);

        _sender = sender;
        _navigationService = navigationService;
        _themeService = themeService;
        _localSettings = localSettings;
        _clipboard = clipboard;
        _notifications = notifications;
        _backendClient = backendClient;
        _backendOptions = backendOptions.Value;
        _activityLog = activityLog;
        _moduleRegistry = moduleRegistry;
        _moduleSteps = [.. moduleSteps];
        // The committed appsettings.json ships without a Backend section; in that state the bound
        // options only hold placeholder defaults, so the card must not present them as a real backend.
        _isBackendConfigured = configuration
            .GetSection(BackendOptions.SectionName)
            .Exists();

        GreetingText = BuildGreeting(displayName: null);
        DateText = DateTime.Now.ToString("dddd, MMMM d");
        StepsProgressText = string.Empty;
        ModulesHeader = "FEATURE MODULES";
        ActivityPageLabel = string.Empty;
        DetachPrompt = string.Empty;
        BackendStatusText = _isBackendConfigured ? "Checking..." : "Not configured";
        IsBackendNeutral = true;
        IsGettingStartedVisible = !_localSettings.Read<bool>(_dismissedKey);
    }

    public ObservableCollection<GettingStartedStep> Steps { get; } = [];

    /// <summary>
    /// The three gates, and how to run each. They are listed on the Home page because a developer
    /// who meets them on the first run meets them before their first pull request rather than in it.
    /// </summary>
    public IReadOnlyList<GateItem> Gates { get; } = [
        new GateItem(
            "archlint",
            "Module boundaries inside the Go server.",
            "cd server && go run ./tools/archlint"),
        new GateItem(
            "__APP_NAME__.ArchitectureTests",
            "The three layers inside the WinUI client.",
            "cd client && dotnet test __APP_NAME__.slnx"),
        new GateItem(
            "buf breaking",
            "The contract between the two halves.",
            "buf breaking --against \".git#branch=main\""),
    ];

    public ObservableCollection<ModuleCardItem> ModuleCards { get; } = [];

    public ObservableCollection<HomeActivityItem> Activity { get; } = [];

    public ObservableCollection<DetachedModuleListItem> DetachedModules { get; } = [];

    public bool HasNoActivity => !HasActivity;

    public bool HasNoDetachedModules => !HasDetachedModules;

    public string BackendEndpointText =>
        !_isBackendConfigured
            ? "—"
            : Uri.TryCreate(_backendOptions.BaseAddress, UriKind.Absolute, out var uri)
                ? $"{uri.Host}:{uri.Port}"
                : _backendOptions.BaseAddress;

    /// <summary>What to run when the backend does not answer. Shown only while it does not.</summary>
    public string StartBackendCommand => _startBackendCommand;

    public string BackendProtocolText =>
        !_isBackendConfigured
            ? "—"
            : _backendOptions.Protocol == BackendProtocol.Grpc ? "gRPC" : "HTTP";

    public string ClientVersionText
    {
        get {
            var version = typeof(HomeViewModel).Assembly.GetName().Version;

            return version is null ? "v1.0.0" : $"v{version.ToString(3)}";
        }
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        var session = await _sender.Send(new GetCurrentSessionQuery());
        IsAuthenticated = session.IsAuthenticated;
        AvatarName = session.DisplayName;
        GreetingText = BuildGreeting(session.DisplayName);
        DateText = DateTime.Now.ToString("dddd, MMMM d");
        IsGettingStartedVisible = !_localSettings.Read<bool>(_dismissedKey);
        RebuildModuleCards();
        LoadActivity();
        // The checklist reads the backend's state and the modules' own, so it is built after the
        // probe rather than before it: a checklist built first would tick on the next visit.
        await PingBackendAsync();
        await RebuildStepsAsync();
    }

    /// <summary>Probes the backend again and re-reads the checklist against the answer.</summary>
    [RelayCommand]
    private async Task RetryBackendAsync()
    {
        await PingBackendAsync();
        await RebuildStepsAsync();
    }

    /// <summary>Puts a shell command on the clipboard and says so.</summary>
    [RelayCommand]
    private void Copy(string command)
    {
        if (string.IsNullOrEmpty(command))
        {
            return;
        }

        _clipboard.SetText(command);
        _notifications.Show($"Copied: {command}");
    }

    /// <summary>
    /// Copies a step's command and counts the step as done. Nothing here can watch a terminal, so
    /// taking the command is the last thing this app sees of the step — and treating that as the
    /// answer beats a checkbox that never ticks.
    /// </summary>
    [RelayCommand]
    private async Task CopyStepAsync(GettingStartedStep step)
    {
        if (step is not { HasCommand: true })
        {
            return;
        }

        Copy(step.Command);
        MarkDone(step.Id);
        await RebuildStepsAsync();
    }

    [RelayCommand]
    private void DismissGettingStarted()
    {
        IsGettingStartedVisible = false;
        _localSettings.Save(_dismissedKey, true);
    }

    /// <summary>
    /// Acts on a checklist row: opens the module a module row belongs to, and marks a row whose
    /// only outcome is having read something.
    /// </summary>
    [RelayCommand]
    private async Task OpenStepAsync(GettingStartedStep step)
    {
        if (step is null)
        {
            return;
        }

        if (step.Id == _architectureStepId)
        {
            MarkDone(step.Id);
            await RebuildStepsAsync();

            return;
        }

        if (step.Id.StartsWith(_modulePrefix, StringComparison.Ordinal))
        {
            OpenModulePage(step.Id[_modulePrefix.Length..]);
        }
    }

    /// <summary>Navigates to a module's first page, and does nothing for one that has none.</summary>
    private void OpenModulePage(string moduleName)
    {
        var module = _moduleRegistry.Attached.FirstOrDefault(item => item.Name == moduleName);

        if (module is null)
        {
            return;
        }

        var pages = module.GetNavigationItems();

        if (pages.Count > 0)
        {
            _navigationService.NavigateTo(_navigationService.GetPageKey(pages[0].PageType));
        }
    }

    [RelayCommand]
    private void OpenModule(ModuleCardItem module)
    {
        if (module.IsWithheld)
        {
            // A withheld module has nothing to navigate to; the click is swallowed here rather than
            // failing on the page's first request.
            return;
        }

        if (module is not null)
        {
            _navigationService.NavigateTo(module.PageKey);
        }
    }

    [RelayCommand]
    private void AttachModule(DetachedModuleListItem module)
    {
        if (module is null)
        {
            return;
        }

        // Cannot fail from here: the dialog only lists modules the registry knows about. The
        // broadcast message rebuilds the tiles and the nav rail.
        _ = _moduleRegistry.Attach(module.Name);
        PrepareAttachModules();
    }

    [RelayCommand]
    private void ActivityPrevPage()
    {
        ShowActivityPage(_activityPage - 1);
    }

    [RelayCommand]
    private void ActivityNextPage()
    {
        ShowActivityPage(_activityPage + 1);
    }

    [RelayCommand]
    private void OpenLink(string url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return;
        }

        using var process = Process.Start(
            new ProcessStartInfo { FileName = url, UseShellExecute = true });
    }

    /// <summary>Stages a tile for detachment and builds the confirmation prompt.</summary>
    public void PrepareDetach(ModuleCardItem module)
    {
        _pendingDetach = module;
        DetachPrompt = $"Detach {module.Name}? Its pages leave the nav rail and this page. " +
            "You can re-attach it any time from “Register your module”.";
    }

    /// <summary>Detaches the staged module. The broadcast message rebuilds shell and tiles.</summary>
    public void ConfirmDetach()
    {
        if (_pendingDetach is { CanDetach: true } module)
        {
            _ = _moduleRegistry.Detach(module.ModuleName);
        }

        _pendingDetach = null;
    }

    /// <summary>Fills the register dialog with the modules that are currently detached.</summary>
    public void PrepareAttachModules()
    {
        DetachedModules.Clear();
        foreach (var module in _moduleRegistry.All)
        {
            if (_moduleRegistry.IsAttached(module.Name))
            {
                continue;
            }

            bool withheld = _moduleRegistry.IsWithheld(module.Name);
            DetachedModules.Add(new DetachedModuleListItem(
                module.Name,
                module.Descriptor.DisplayName,
                withheld
                    ? "Your account is not assigned this module. Ask an administrator."
                    : module.Descriptor.Description,
                withheld));
        }

        HasDetachedModules = DetachedModules.Count > 0;
    }

    /// <summary>
    /// Rebuilds the checklist of docs/pivot/05-PHASE-4-UI.md §2.1. The first rows read real state —
    /// the backend probe, and each module's own answer about itself — and the rest are marked by
    /// the developer acting on them, because nothing in this process can watch a terminal.
    /// </summary>
    /// <remarks>
    /// The module rows come from the container rather than from a list here. A module the scaffold
    /// left out, or that <c>kakehashi remove module</c> took back, is not there to register one,
    /// so the checklist shrinks to match the project without anybody editing it — which is the
    /// whole of what --bare needs (§2.2).
    /// </remarks>
    private async Task RebuildStepsAsync()
    {
        var steps = new List<GettingStartedStep>
        {
            new(
                _backendStepId,
                "Backend connected",
                _isBackendConfigured
                    ? $"{BackendEndpointText} over {BackendProtocolText} · {BackendStatusText}"
                    : "No Backend section in appsettings.json yet.",
                IsBackendConnected,
                HasAction: false),
        };

        foreach (var module in _moduleSteps)
        {
            steps.Add(new GettingStartedStep(
                _modulePrefix + module.ModuleName,
                module.Title,
                module.Subtitle,
                await IsDoneAsync(module),
                HasAction: true));
        }

        steps.Add(new GettingStartedStep(
            _architectureStepId,
            "Read docs/ARCHITECTURE.md",
            "Why the shape is the shape: the modules, the layers, and the contract between them.",
            IsMarkedDone(_architectureStepId),
            HasAction: true));

        bool added = _moduleRegistry.All.Count > _shippedModuleCount;
        steps.Add(new GettingStartedStep(
            _addModuleStepId,
            "Add your first module",
            "Both halves, the proto contract and the wiring, with all three gates still green.",
            added || IsMarkedDone(_addModuleStepId),
            HasAction: false,
            Command: "kakehashi add module orders"));

        // Only while the project still holds what it was given. Once there is a module somebody
        // wrote, "take the example back out" is advice they have already outgrown.
        if (!added)
        {
            foreach (var module in _moduleSteps)
            {
                string id = _removePrefix + module.ModuleName;
                steps.Add(new GettingStartedStep(
                    id,
                    $"Remove the {module.ModuleName} example when you no longer need it",
                    "It leaves nothing behind: the proto, both module trees, the tests and the wiring.",
                    IsMarkedDone(id),
                    HasAction: false,
                    // The generator derives a module's name by capitalising its unit id, so the id
                    // this command needs is the name in lower case, for the example and for every
                    // module `kakehashi add module` writes.
                    Command: "kakehashi remove module " + module.ModuleName.ToLowerInvariant()));
            }
        }

        Steps.Clear();
        foreach (var step in steps)
        {
            Steps.Add(step);
        }

        int done = steps.Count(step => step.IsDone);
        StepsTotal = steps.Count;
        StepsProgress = done;
        StepsProgressText = $"{done} of {steps.Count} complete";
    }

    /// <summary>
    /// Asks a module whether its step is done, and reads a module that cannot answer as not done.
    /// The usual reason is a backend that is not running, which is the row above's problem.
    /// </summary>
    private static async Task<bool> IsDoneAsync(IGettingStartedStep step)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            return await step.IsDoneAsync(timeout.Token);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private bool IsMarkedDone(string stepId)
    {
        return _localSettings.Read<bool>(_stepDoneKeyPrefix + stepId);
    }

    private void MarkDone(string stepId)
    {
        _localSettings.Save(_stepDoneKeyPrefix + stepId, true);
    }

    private void RebuildModuleCards()
    {
        ModuleCards.Clear();
        foreach (var module in _moduleRegistry.Attached)
        {
            foreach (var item in module.GetNavigationItems())
            {
                ModuleCards.Add(CreateModuleCard(module, item));
            }
        }

        // Withheld modules are never in Attached — the rail must not offer a page the server refuses
        // — but they are drawn here, locked, so the user can see what to ask an administrator for.
        foreach (var module in _moduleRegistry.All)
        {
            if (!_moduleRegistry.IsWithheld(module.Name))
            {
                continue;
            }
            foreach (var item in module.GetNavigationItems())
            {
                ModuleCards.Add(CreateModuleCard(module, item));
            }
        }

        ModuleCards.Add(new ModuleCardItem(
            _navigationService.GetPageKey(typeof(SettingsPage)),
            "Settings",
            "",
            "CORE",
            "Theme, error reporting and diagnostics for the application shell.",
            $"Theme: {ThemeText(_themeService.Theme)}",
            string.Empty,
            CanDetach: false));
        ModulesHeader = $"FEATURE MODULES ({ModuleCards.Count})";
    }

    private ModuleCardItem CreateModuleCard(IModule module, NavigationItem item)
    {
        var (badge, foot) =
            item.PageType == typeof(AccountPage)
                ? ("CORE", IsAuthenticated ? "Active session" : "Signed out")
                : item.PageType.Name == "ProductsPage"
                    ? ("CORE", $"{module.Name} module")
                    : ("MODULE", $"{module.Name} module");
        string glyph = string.IsNullOrEmpty(item.IconGlyph) ? "" : item.IconGlyph;
        bool withheld = _moduleRegistry.IsWithheld(module.Name);
        bool granted = _moduleRegistry.IsGranted(module.Name);

        if (withheld)
        {
            (badge, foot) = ("LOCKED", "Ask an administrator for access");
        }
        else if (granted)
        {
            foot = "Assigned by an administrator";
        }

        return new ModuleCardItem(
            _navigationService.GetPageKey(item.PageType),
            item.Title,
            glyph,
            badge,
            module.Descriptor.Description,
            foot,
            module.Name,
            // A granted module cannot be detached — the grant is not the user's to give back; a
            // withheld one has nothing to detach.
            CanDetach: !module.Descriptor.IsRequired && !granted && !withheld,
            IsWithheld: withheld,
            IsGranted: granted);
    }

    private async Task PingBackendAsync()
    {
        if (!_isBackendConfigured)
        {
            BackendStatusText = "Not configured";
            IsBackendNeutral = true;
            IsBackendConnected = false;
            IsBackendOffline = false;

            return;
        }

        IsBackendNeutral = true;
        IsBackendConnected = false;
        IsBackendOffline = false;
        BackendStatusText = "Checking...";
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var stopwatch = Stopwatch.StartNew();
            await _backendClient.PingAsync(new PingRequest("home"), timeout.Token);
            stopwatch.Stop();
            BackendStatusText = $"Connected · {stopwatch.ElapsedMilliseconds}ms";
            IsBackendConnected = true;
        }
        catch (Exception)
        {
            // Any transport failure (offline, timeout, TLS, DNS) renders as the offline state.
            BackendStatusText = "Offline";
            IsBackendOffline = true;
        }
        finally
        {
            IsBackendNeutral = !IsBackendConnected && !IsBackendOffline;
        }
    }

    private void LoadActivity()
    {
        _allActivity = [.. _activityLog
            .GetRecent()
            .Select(ToActivityItem)];
        HasActivity = _allActivity.Count > 0;
        ShowActivityPage(1);
    }

    private void ShowActivityPage(int page)
    {
        var pageCount = Math.Max(1, (int)Math.Ceiling(_allActivity.Count / (double)_pageSize));
        _activityPage = Math.Clamp(page, 1, pageCount);
        Activity.Clear();
        foreach (var item in _allActivity
            .Skip((_activityPage - 1) * _pageSize)
            .Take(_pageSize))
        {
            Activity.Add(item);
        }

        HasActivityPaging = _allActivity.Count > _pageSize;
        ActivityPageLabel = $"{_activityPage} / {pageCount}";
    }

    private static HomeActivityItem ToActivityItem(AppActivityEntry entry)
    {
        var (glyph, isPositive) = entry.Kind switch {
            AppActivityLog.SignedInKind => ("", true),
            AppActivityLog.SignedOutKind => ("", false),
            AppActivityLog.AppUpdatedKind => ("", false),
            AppActivityLog.ThemeChangedKind => ("", false),
            _ => ("", false),
        };

        return new HomeActivityItem(
            entry.Title,
            entry.Detail,
            FormatRelative(entry.OccurredAt),
            glyph,
            isPositive,
            IsAlert: false);
    }

    private static string BuildGreeting(string? displayName)
    {
        string greeting = DateTime.Now.Hour switch {
            < 12 => "Good morning",
            < 18 => "Good afternoon",
            _ => "Good evening",
        };
        string? firstName = displayName?
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

        return string.IsNullOrEmpty(firstName) ? greeting : $"{greeting}, {firstName}";
    }

    private static string ThemeText(ElementTheme theme)
    {
        return theme switch {
            ElementTheme.Light => "Light",
            ElementTheme.Dark => "Dark",
            _ => "System",
        };
    }

    private static string FormatRelative(DateTimeOffset at)
    {
        var span = DateTimeOffset.UtcNow - at;

        if (span < TimeSpan.FromMinutes(1))
        {
            return "now";
        }

        if (span < TimeSpan.FromHours(1))
        {
            return $"{(int)span.TotalMinutes}m ago";
        }

        if (span < TimeSpan.FromDays(1))
        {
            return $"{(int)span.TotalHours}h ago";
        }

        if (span < TimeSpan.FromDays(30))
        {
            return $"{(int)span.TotalDays}d ago";
        }

        return at
            .ToLocalTime()
            .ToString("MMM d, yyyy");
    }
}

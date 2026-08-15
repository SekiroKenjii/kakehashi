using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kakehashi.App.Infrastructure.Backend;
using Kakehashi.App.Infrastructure.Backend.Contracts;
using Kakehashi.App.Services;
using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.Modules.Auth.Application.Sessions.Queries.GetCurrentSession;
using Kakehashi.Modules.Auth.UI.Views;
using Kakehashi.UI.Contracts;
using Kakehashi.UI.Contracts.Services.Platform;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.UI.Xaml;

namespace Kakehashi.App.UI;

public sealed record GettingStartedStep(
    string Id, string Title, string Subtitle, bool IsDone, bool HasAction)
{
    public bool IsNotDone => !IsDone;

    public bool ShowsChevron => HasAction && !IsDone;
}

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
    private const string _exploreStepDoneKey = "Home.ExploreModuleStepDone";
    private const string _themeStepDoneKey = "Home.CustomizeThemeStepDone";
    private const string _signInStepId = "signin";
    private const string _exploreStepId = "explore";
    private const string _themeStepId = "theme";
    private const string _registerStepId = "register";
    /// <summary>
    /// How many modules ship with the template: Notes, Activity and Auth. A higher count means the
    /// developer registered their own. Keep it equal to the shipped modules, or "register your
    /// first module" reports itself complete on a fresh install.
    /// </summary>
    private const int _shippedModuleCount = 3;
    private const int _pageSize = 5;

    private readonly ISender _sender;
    private readonly INavigationService _navigationService;
    private readonly IThemeService _themeService;
    private readonly ILocalSettingsService _localSettings;
    private readonly IBackendClient _backendClient;
    private readonly BackendOptions _backendOptions;
    private readonly IModuleRegistry _moduleRegistry;
    private readonly AppActivityLog _activityLog;
    private readonly bool _isBackendConfigured;
    private List<HomeActivityItem> _allActivity = [];
    private int _activityPage = 1;
    private DateTimeOffset? _signedInAtUtc;
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
        IBackendClient backendClient,
        IOptions<BackendOptions> backendOptions,
        IConfiguration configuration,
        AppActivityLog activityLog,
        IModuleRegistry moduleRegistry)
    {
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(navigationService);
        ArgumentNullException.ThrowIfNull(themeService);
        ArgumentNullException.ThrowIfNull(localSettings);
        ArgumentNullException.ThrowIfNull(backendClient);
        ArgumentNullException.ThrowIfNull(backendOptions);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(activityLog);
        ArgumentNullException.ThrowIfNull(moduleRegistry);

        _sender = sender;
        _navigationService = navigationService;
        _themeService = themeService;
        _localSettings = localSettings;
        _backendClient = backendClient;
        _backendOptions = backendOptions.Value;
        _activityLog = activityLog;
        _moduleRegistry = moduleRegistry;
        // The committed appsettings.json ships without a Backend section; in that state the bound
        // options only hold placeholder defaults, so the card must not present them as a real backend.
        _isBackendConfigured = configuration
            .GetSection(BackendOptions.SectionName)
            .Exists();

        GreetingText = BuildGreeting(displayName: null);
        DateText = DateTime.Now.ToString("dddd, MMMM d");
        StepsProgressText = "0 of 4 complete";
        ModulesHeader = "FEATURE MODULES";
        ActivityPageLabel = string.Empty;
        DetachPrompt = string.Empty;
        BackendStatusText = _isBackendConfigured ? "Checking..." : "Not configured";
        IsBackendNeutral = true;
        IsGettingStartedVisible = !_localSettings.Read<bool>(_dismissedKey);
    }

    public ObservableCollection<GettingStartedStep> Steps { get; } = [];

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
        _signedInAtUtc = session.SignedInAtUtc;
        GreetingText = BuildGreeting(session.DisplayName);
        DateText = DateTime.Now.ToString("dddd, MMMM d");
        IsGettingStartedVisible = !_localSettings.Read<bool>(_dismissedKey);
        RebuildSteps();
        RebuildModuleCards();
        LoadActivity();
        await PingBackendAsync();
    }

    [RelayCommand]
    private void DismissGettingStarted()
    {
        IsGettingStartedVisible = false;
        _localSettings.Save(_dismissedKey, true);
    }

    [RelayCommand]
    private void OpenStep(GettingStartedStep step)
    {
        switch (step?.Id)
        {
            case _signInStepId:
                _navigationService.NavigateTo(_navigationService.GetPageKey(typeof(AccountPage)));
                break;
            case _exploreStepId:
                _localSettings.Save(_exploreStepDoneKey, true);
                _navigationService.NavigateTo(_navigationService.GetPageKey(typeof(AccountPage)));
                break;
            case _themeStepId:
                _localSettings.Save(_themeStepDoneKey, true);
                _navigationService.NavigateTo(_navigationService.GetPageKey(typeof(SettingsPage)));
                break;
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

    private void RebuildSteps()
    {
        bool exploreDone = _localSettings.Read<bool>(_exploreStepDoneKey);
        bool themeDone = _localSettings.Read<bool>(_themeStepDoneKey)
            || _themeService.Theme != ElementTheme.Default;
        bool registerDone = _moduleRegistry.All.Count > _shippedModuleCount;
        string signInSubtitle = IsAuthenticated && _signedInAtUtc is { } signedInAt
            ? $"OAuth 2.0 browser flow · completed {FormatRelative(signedInAt)}"
            : "OAuth 2.0 browser flow · sign in from the Account page";

        Steps.Clear();
        Steps.Add(new GettingStartedStep(
            _signInStepId, "Sign in with company SSO", signInSubtitle, IsAuthenticated,
            HasAction: true));
        Steps.Add(new GettingStartedStep(
            _exploreStepId, "Explore the sample module",
            "Open the Account module to view active sessions, recent sign-ins and more.",
            exploreDone, HasAction: true));
        Steps.Add(new GettingStartedStep(
            _themeStepId, "Customize the app theme",
            "Light / Dark / System — in Settings → Appearance", themeDone, HasAction: true));
        Steps.Add(new GettingStartedStep(
            _registerStepId, "Register your first module",
            "Implement IModule and it appears in the nav rail", registerDone, HasAction: false));

        int done = Steps.Count(step => step.IsDone);
        StepsProgress = done;
        StepsProgressText = $"{done} of {Steps.Count} complete";
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

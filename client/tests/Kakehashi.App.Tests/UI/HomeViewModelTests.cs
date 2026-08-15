using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kakehashi.App.Infrastructure.Backend;
using Kakehashi.App.Infrastructure.Backend.Contracts;
using Kakehashi.App.Services;
using Kakehashi.App.UI;
using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.Modules.Auth.Application.Sessions;
using Kakehashi.Modules.Auth.Application.Sessions.Queries.GetCurrentSession;
using Kakehashi.UI.Contracts;
using Kakehashi.UI.Contracts.Services.Platform;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using NSubstitute;
using Xunit;

namespace Kakehashi.App.Tests.UI;

/// <summary>
/// Unit tests for <see cref="HomeViewModel"/>: the session-aware greeting, the getting-started
/// checklist, the backend probe states, the paged activity feed, and the module tiles with
/// attach/detach. Dependencies are substituted; the activity log is a real instance over an
/// in-memory store so feed paging is exercised end to end.
/// </summary>
public sealed class HomeViewModelTests
{
    private const string _activityEntriesKey = "App.ActivityLog";

    private readonly ISender _sender = Substitute.For<ISender>();
    private readonly INavigationService _navigation = Substitute.For<INavigationService>();
    private readonly IThemeService _theme = Substitute.For<IThemeService>();
    private readonly InMemoryLocalSettings _settings = new();
    private readonly IBackendClient _backend = Substitute.For<IBackendClient>();
    private readonly BackendOptions _backendOptions = new();
    private readonly InMemoryLocalSettings _activitySettings = new();
    private readonly IModuleRegistry _registry = Substitute.For<IModuleRegistry>();
    private SessionDto _session = new(false, null, null, null, null, []);
    private IReadOnlyList<IModule> _all = [];
    private IReadOnlyList<IModule> _attached = [];

    public HomeViewModelTests()
    {
        _sender.Send(Arg.Any<GetCurrentSessionQuery>()).Returns(_ => Task.FromResult(_session));
        _registry.All.Returns(_ => _all);
        _registry.Attached.Returns(_ => _attached);
    }

    [Fact]
    public async Task Load_WhenSignedIn_GreetsByFirstName()
    {
        _session = new SessionDto(
            true, "Vo Thuong", "vo@example.com", null, DateTimeOffset.UtcNow.AddHours(-2), []);
        var viewModel = CreateViewModel();

        await viewModel.LoadCommand.ExecuteAsync(parameter: null);

        Assert.True(viewModel.IsAuthenticated);
        Assert.Equal("Vo Thuong", viewModel.AvatarName);
        Assert.StartsWith("Good ", viewModel.GreetingText);
        Assert.EndsWith(", Vo", viewModel.GreetingText);
    }

    [Fact]
    public async Task Load_WhenSignedOut_GreetingHasNoName()
    {
        var viewModel = CreateViewModel();

        await viewModel.LoadCommand.ExecuteAsync(parameter: null);

        Assert.False(viewModel.IsAuthenticated);
        Assert.StartsWith("Good ", viewModel.GreetingText);
        Assert.DoesNotContain(",", viewModel.GreetingText);
    }

    [Fact]
    public async Task Load_GettingStarted_MarksSignInDoneAndReportsProgress()
    {
        _session = new SessionDto(
            true, "Vo", null, null, DateTimeOffset.UtcNow.AddHours(-1), []);
        // Exactly what the template ships — Notes, Activity and Auth — so the step is not done.
        _all = [Module("Notes"), Module("Activity"), Module("Auth")];
        var viewModel = CreateViewModel();

        await viewModel.LoadCommand.ExecuteAsync(parameter: null);

        Assert.Equal(4, viewModel.Steps.Count);
        Assert.True(viewModel.Steps.Single(s => s.Id == "signin").IsDone);
        Assert.False(viewModel.Steps.Single(s => s.Id == "register").IsDone);
        Assert.Equal(1d, viewModel.StepsProgress);
        Assert.Equal("1 of 4 complete", viewModel.StepsProgressText);
    }

    [Fact]
    public async Task Load_GettingStarted_ThemeStepDoneWhenThemeNotDefault()
    {
        _theme.Theme.Returns(ElementTheme.Dark);
        var viewModel = CreateViewModel();

        await viewModel.LoadCommand.ExecuteAsync(parameter: null);

        Assert.True(viewModel.Steps.Single(s => s.Id == "theme").IsDone);
    }

    [Fact]
    public async Task Load_GettingStarted_RegisterStepDoneWhenExtraModuleComposed()
    {
        // The three shipped modules plus one somebody wrote.
        _all = [Module("Notes"), Module("Activity"), Module("Auth"), Module("Reports")];
        var viewModel = CreateViewModel();

        await viewModel.LoadCommand.ExecuteAsync(parameter: null);

        Assert.True(viewModel.Steps.Single(s => s.Id == "register").IsDone);
    }

    [Fact]
    public async Task Load_BackendNotConfigured_ShowsNeutralNotConfigured()
    {
        var viewModel = CreateViewModel(backendConfigured: false);

        await viewModel.LoadCommand.ExecuteAsync(parameter: null);

        Assert.True(viewModel.IsBackendNeutral);
        Assert.False(viewModel.IsBackendConnected);
        Assert.Equal("Not configured", viewModel.BackendStatusText);
        Assert.Equal("—", viewModel.BackendEndpointText);
        Assert.Equal("—", viewModel.BackendProtocolText);
    }

    [Fact]
    public async Task Load_BackendConfiguredAndReachable_ShowsConnected()
    {
        _backend.PingAsync(Arg.Any<PingRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PingResponse("home", DateTimeOffset.UtcNow)));
        var viewModel = CreateViewModel(backendConfigured: true);

        await viewModel.LoadCommand.ExecuteAsync(parameter: null);

        Assert.True(viewModel.IsBackendConnected);
        Assert.False(viewModel.IsBackendNeutral);
        Assert.StartsWith("Connected", viewModel.BackendStatusText);
        Assert.Equal("localhost:5001", viewModel.BackendEndpointText);
        Assert.Equal("HTTP", viewModel.BackendProtocolText);
    }

    [Fact]
    public async Task Load_BackendConfiguredButUnreachable_ShowsOffline()
    {
        _backend.PingAsync(Arg.Any<PingRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<PingResponse>(new InvalidOperationException()));
        var viewModel = CreateViewModel(backendConfigured: true);

        await viewModel.LoadCommand.ExecuteAsync(parameter: null);

        Assert.True(viewModel.IsBackendOffline);
        Assert.False(viewModel.IsBackendConnected);
        Assert.Equal("Offline", viewModel.BackendStatusText);
    }

    [Fact]
    public async Task Load_ActivityFeed_PagesFivePerPage()
    {
        SeedActivity(7);
        var viewModel = CreateViewModel();

        await viewModel.LoadCommand.ExecuteAsync(parameter: null);

        Assert.True(viewModel.HasActivity);
        Assert.Equal(5, viewModel.Activity.Count);
        Assert.True(viewModel.HasActivityPaging);
        Assert.Equal("1 / 2", viewModel.ActivityPageLabel);

        viewModel.ActivityNextPageCommand.Execute(parameter: null);
        Assert.Equal(2, viewModel.Activity.Count);
        Assert.Equal("2 / 2", viewModel.ActivityPageLabel);

        viewModel.ActivityPrevPageCommand.Execute(parameter: null);
        Assert.Equal(5, viewModel.Activity.Count);
        Assert.Equal("1 / 2", viewModel.ActivityPageLabel);
    }

    [Fact]
    public async Task Load_NoActivity_ReportsEmptyAndNoPaging()
    {
        var viewModel = CreateViewModel();

        await viewModel.LoadCommand.ExecuteAsync(parameter: null);

        Assert.False(viewModel.HasActivity);
        Assert.True(viewModel.HasNoActivity);
        Assert.False(viewModel.HasActivityPaging);
    }

    [Fact]
    public async Task Load_BuildsTilesFromAttachedModulesPlusSettings()
    {
        _attached = [Module("Notes", navItem: new NavigationItem("Products", "", typeof(HomePage)))];
        var viewModel = CreateViewModel();

        await viewModel.LoadCommand.ExecuteAsync(parameter: null);

        Assert.Equal(2, viewModel.ModuleCards.Count);
        Assert.Equal("FEATURE MODULES (2)", viewModel.ModulesHeader);
        Assert.True(viewModel.ModuleCards.Single(c => c.ModuleName == "Notes").CanDetach);
        Assert.False(viewModel.ModuleCards.Single(c => c.Name == "Settings").CanDetach);
    }

    [Fact]
    public async Task Load_RequiredModuleTile_CannotDetach()
    {
        _attached = [Module(
            "Auth", isRequired: true, navItem: new NavigationItem("Account", "", typeof(HomePage)))];
        var viewModel = CreateViewModel();

        await viewModel.LoadCommand.ExecuteAsync(parameter: null);

        Assert.False(viewModel.ModuleCards.Single(c => c.ModuleName == "Auth").CanDetach);
    }

    [Fact]
    public void ConfirmDetach_DetachableModule_DetachesByModuleName()
    {
        var viewModel = CreateViewModel();
        var card = new ModuleCardItem(
            "Products", "Products", "", "MODULE", "desc", "Notes module", "Notes", CanDetach: true);

        viewModel.PrepareDetach(card);
        Assert.Contains("Products", viewModel.DetachPrompt);
        viewModel.ConfirmDetach();

        _registry.Received(1).Detach("Notes");
    }

    [Fact]
    public void ConfirmDetach_NonDetachableModule_DoesNothing()
    {
        var viewModel = CreateViewModel();
        var card = new ModuleCardItem(
            "Settings", "Settings", "", "CORE", "desc", "Theme: System", "", CanDetach: false);

        viewModel.PrepareDetach(card);
        viewModel.ConfirmDetach();

        _registry.DidNotReceive().Detach(Arg.Any<string>());
    }

    [Fact]
    public void PrepareAttachModules_ListsOnlyDetachedModules()
    {
        _all = [Module("Notes"), Module("Reports")];
        _registry.IsAttached("Notes").Returns(true);
        _registry.IsAttached("Reports").Returns(false);
        var viewModel = CreateViewModel();

        viewModel.PrepareAttachModules();

        Assert.True(viewModel.HasDetachedModules);
        Assert.Equal("Reports", Assert.Single(viewModel.DetachedModules).Name);
    }

    [Fact]
    public void AttachModule_AttachesThroughRegistry()
    {
        var viewModel = CreateViewModel();

        viewModel.AttachModuleCommand.Execute(new DetachedModuleListItem("Reports", "Reports", "desc"));

        _registry.Received(1).Attach("Reports");
    }

    private HomeViewModel CreateViewModel(bool backendConfigured = false)
    {
        var configData = new Dictionary<string, string?>();

        if (backendConfigured)
        {
            configData["Backend:BaseAddress"] = _backendOptions.BaseAddress;
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        return new HomeViewModel(
            _sender,
            _navigation,
            _theme,
            _settings,
            _backend,
            new StubOptions<BackendOptions>(_backendOptions),
            configuration,
            new AppActivityLog(_activitySettings),
            _registry);
    }

    private void SeedActivity(int count)
    {
        var entries = new List<AppActivityEntry>();
        for (int i = 0; i < count; i++)
        {
            entries.Add(new AppActivityEntry(
                AppActivityLog.SignedInKind, $"e{i}", "detail", DateTimeOffset.UtcNow));
        }

        _activitySettings.Save(_activityEntriesKey, entries);
    }

    private static FakeModule Module(
        string name, bool isRequired = false, NavigationItem? navItem = null)
    {
        return new FakeModule(name, isRequired, navItem);
    }

    /* --- The assignment lock --- */

    [Fact]
    public async Task Load_AWithheldModule_GetsALockedTileRatherThanVanishing()
    {
        // A withheld module must keep a tile: one that disappears tells the user nothing and looks
        // the same as one that was never built.
        var notes = Module("Notes", navItem: new NavigationItem("Notes", "", typeof(HomePage)));
        _all = [notes];
        _attached = [];  // withheld modules are never attached
        _registry.IsWithheld("Notes").Returns(true);
        var viewModel = CreateViewModel();

        await viewModel.LoadCommand.ExecuteAsync(parameter: null);

        var tile = viewModel.ModuleCards.Single(card => card.ModuleName == "Notes");
        Assert.True(tile.IsWithheld);
        Assert.False(tile.CanOpen);
        Assert.Equal("LOCKED", tile.Badge);
        Assert.Contains("administrator", tile.FootText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Load_AWithheldModule_OffersNoDetachEither()
    {
        // Detaching something you were never given is a button that means nothing.
        var notes = Module("Notes", navItem: new NavigationItem("Notes", "", typeof(HomePage)));
        _all = [notes];
        _attached = [];
        _registry.IsWithheld("Notes").Returns(true);
        var viewModel = CreateViewModel();

        await viewModel.LoadCommand.ExecuteAsync(parameter: null);

        Assert.False(viewModel.ModuleCards.Single(card => card.ModuleName == "Notes").CanDetach);
    }

    [Fact]
    public async Task Load_AGrantedModule_IsUsableButNotDetachable()
    {
        var notes = Module("Notes", navItem: new NavigationItem("Notes", "", typeof(HomePage)));
        _all = [notes];
        _attached = [notes];
        _registry.IsGranted("Notes").Returns(true);
        var viewModel = CreateViewModel();

        await viewModel.LoadCommand.ExecuteAsync(parameter: null);

        var tile = viewModel.ModuleCards.Single(card => card.ModuleName == "Notes");
        Assert.True(tile.CanOpen);
        Assert.True(tile.IsGranted);
        // Granted means an administrator assigned the module; detaching it is not the user's call.
        Assert.False(tile.CanDetach);
        Assert.Contains("administrator", tile.FootText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Load_AnUngovernedModule_IsUnchanged()
    {
        // "Open" is not "granted". A module nobody restricted stays entirely the user's own choice;
        // assignments must have no visible effect on a deployment that does not use them.
        var notes = Module("Notes", navItem: new NavigationItem("Notes", "", typeof(HomePage)));
        _all = [notes];
        _attached = [notes];
        var viewModel = CreateViewModel();

        await viewModel.LoadCommand.ExecuteAsync(parameter: null);

        var tile = viewModel.ModuleCards.Single(card => card.ModuleName == "Notes");
        Assert.True(tile.CanOpen);
        Assert.True(tile.CanDetach);
        Assert.False(tile.IsWithheld);
        Assert.False(tile.IsGranted);
    }

    [Fact]
    public void PrepareAttachModules_ListsAWithheldModuleWithItsReason()
    {
        var notes = Module("Notes", navItem: new NavigationItem("Notes", "", typeof(HomePage)));
        _all = [notes];
        _registry.IsAttached("Notes").Returns(false);
        _registry.IsWithheld("Notes").Returns(true);
        var viewModel = CreateViewModel();

        viewModel.PrepareAttachModules();

        var row = viewModel.DetachedModules.Single(module => module.Name == "Notes");
        Assert.True(row.IsWithheld);
        Assert.False(row.CanAttach);
        Assert.Contains("administrator", row.Description, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeModule : IModule
    {
        private readonly NavigationItem? _navItem;

        public FakeModule(
            string name, bool isRequired, NavigationItem? navItem, string? assignmentId = null)
        {
            Name = name;
            Descriptor = new ModuleDescriptor(
                $"{name} display", $"{name} description", isRequired, assignmentId);
            _navItem = navItem;
        }

        public string Name { get; }

        public ModuleDescriptor Descriptor { get; }

        public void RegisterServices(IServiceCollection services) { }

        public IReadOnlyList<NavigationItem> GetNavigationItems()
        {
            return _navItem is null ? [] : [_navItem];
        }
    }
}

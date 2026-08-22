using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using __ROOT_NAMESPACE__.App.Plugins;
using __ROOT_NAMESPACE__.App.Services;
using __ROOT_NAMESPACE__.App.UI;
using __ROOT_NAMESPACE__.PluginSdk.Abstractions;
using __ROOT_NAMESPACE__.SharedKernel;
using __ROOT_NAMESPACE__.UI.Contracts;
using __ROOT_NAMESPACE__.UI.Contracts.Services.Platform;
using NSubstitute;
using Xunit;

namespace __ROOT_NAMESPACE__.App.Tests.UI;

/// <summary>
/// Unit tests for <see cref="PluginsViewModel"/>: what the list is made of, what the filters do,
/// and the rule the install prompt exists to enforce.
/// </summary>
/// <remarks>
/// No XAML is constructed. The installer is real but pointed at a temporary directory, because its
/// refusals are part of what is being checked.
/// </remarks>
public sealed class PluginsViewModelTests : IDisposable
{
    private readonly string _root = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName());

    private readonly IModuleRegistry _modules = Substitute.For<IModuleRegistry>();
    private readonly IFileOpenService _files = Substitute.For<IFileOpenService>();
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();
    private readonly IPluginCatalogService _catalogService = Substitute.For<IPluginCatalogService>();
    private readonly PluginCatalog _catalog = new();

    public void Dispose()
    {
        if (System.IO.Directory.Exists(_root))
        {
            System.IO.Directory.Delete(_root, recursive: true);
        }
    }

    private PluginsViewModel CreateViewModel()
    {
        var installer = new PluginInstaller(new PluginPaths(_root), publisher: string.Empty);
        var scaffolder = new PluginScaffolder(_root);

        return new PluginsViewModel(
            _modules, _catalog, installer, _files, _dialogs, scaffolder, _catalogService);
    }

    private static IModule Module(string name, string display, bool required)
    {
        var module = Substitute.For<IModule>();
        module.Name.Returns(name);
        module.Descriptor.Returns(new ModuleDescriptor(display, $"{display} does things.", required));
        module.GetNavigationItems().Returns([]);

        return module;
    }

    private void Compose(params IModule[] modules)
    {
        _modules.All.Returns(modules);
        _modules.IsAttached(Arg.Any<string>()).Returns(true);
    }

    private void AddInstalled(string id, string moduleName, string signature, string version = "1.0.0")
    {
        _catalog.Add(new LoadedPlugin(
            new PluginRecord {
                PluginID = id,
                DisplayName = moduleName,
                InstalledVersion = version,
                Signature = signature,
                Source = "File",
                SizeInBytes = 2 * 1024 * 1024,
            },
            new PluginManifest {
                Id = id,
                ModuleName = moduleName,
                DisplayName = moduleName,
                Description = "An installed thing.",
                EntryAssembly = $"App.Modules.{moduleName}.UI.dll",
            }));
    }

    [Fact]
    public void Load_ShowsBuiltInsAndInstalledInOneList()
    {
        Compose(Module("Notes", "Notes", required: false), Module("Weather", "Weather", required: false));
        AddInstalled("weather", "Weather", nameof(PluginTrustLevel.Unofficial));

        var viewModel = CreateViewModel();
        viewModel.Load();

        Assert.Equal(2, viewModel.Items.Count);
        Assert.Contains(viewModel.Items, item => item.Origin == PluginOrigin.BuiltIn);
        Assert.Contains(viewModel.Items, item => item.Origin == PluginOrigin.Unofficial);
    }

    /// <summary>
    /// A module that is compiled in and a module that was installed must not both appear: the
    /// registry holds the installed one too, and listing it twice would offer two toggles for one
    /// thing.
    /// </summary>
    [Fact]
    public void Load_DoesNotListAnInstalledModuleAsBuiltInAsWell()
    {
        Compose(Module("Weather", "Weather", required: false));
        AddInstalled("weather", "Weather", nameof(PluginTrustLevel.Verified));

        var viewModel = CreateViewModel();
        viewModel.Load();

        Assert.Single(viewModel.Items);
        Assert.Equal(PluginOrigin.Verified, viewModel.Items[0].Origin);
    }

    [Fact]
    public void Load_AnUnofficialPluginCarriesTheWarning()
    {
        Compose();
        AddInstalled("weather", "Weather", nameof(PluginTrustLevel.Unofficial));

        var viewModel = CreateViewModel();
        viewModel.Load();

        Assert.Equal(PluginsViewModel.UnsignedWarning, viewModel.Items[0].Warning);
    }

    [Fact]
    public void Load_AVerifiedPluginCarriesNone()
    {
        Compose();
        AddInstalled("weather", "Weather", nameof(PluginTrustLevel.Verified));

        var viewModel = CreateViewModel();
        viewModel.Load();

        Assert.Equal(string.Empty, viewModel.Items[0].Warning);
    }

    [Fact]
    public void Load_ARequiredModuleCannotBeTurnedOff()
    {
        Compose(Module("Auth", "Account", required: true));

        var viewModel = CreateViewModel();
        viewModel.Load();

        Assert.False(viewModel.Items[0].CanToggle);
    }

    [Fact]
    public void Load_APluginThatDidNotLoadIsListedWithItsReason()
    {
        Compose();
        _catalog.AddFault("weather", "1.0.0", new Error("Plugin.Load.Invalid", "It was not valid."));

        var viewModel = CreateViewModel();
        viewModel.Load();

        Assert.Equal(PluginOrigin.Faulted, viewModel.Items[0].Origin);
        Assert.Equal("It was not valid.", viewModel.Items[0].Warning);
    }

    [Fact]
    public void Filter_NarrowsToOneOrigin()
    {
        Compose(Module("Notes", "Notes", required: false), Module("Weather", "Weather", required: false));
        AddInstalled("weather", "Weather", nameof(PluginTrustLevel.Unofficial));

        var viewModel = CreateViewModel();
        viewModel.Load();
        viewModel.Filter = "Unofficial";

        Assert.Single(viewModel.Items);
        Assert.Equal("Weather", viewModel.Items[0].DisplayName);
    }

    [Fact]
    public void Search_MatchesTheNameAndTheModule()
    {
        Compose(Module("Notes", "Notes", required: false), Module("Activity", "Activity", required: false));

        var viewModel = CreateViewModel();
        viewModel.Load();
        viewModel.SearchText = "activ";

        Assert.Single(viewModel.Items);
    }

    [Fact]
    public void Toggle_AsksTheRegistryAndReportsWhatItRefused()
    {
        Compose(Module("Auth", "Account", required: true));
        _modules.Detach("Auth").Returns(Result.Failure(new Error("Modules.Required", "Required.")));

        var viewModel = CreateViewModel();
        viewModel.Load();
        viewModel.Toggle(viewModel.Items[0]);

        _ = _modules.Received(1).Detach("Auth");
        Assert.True(viewModel.HasError);
        Assert.Equal("Required.", viewModel.ErrorMessage);
    }

    /// <summary>
    /// The whole point of the install prompt: an unverified package cannot be installed without the
    /// user saying so, and the button that would do it is not available until they have.
    /// </summary>
    [Fact]
    public void CanInstallPending_IsFalseUntilAnUnverifiedPackageIsConsentedTo()
    {
        Compose();
        var viewModel = CreateViewModel();

        Assert.False(viewModel.CanInstallPending);
        Assert.False(viewModel.ConsentRequired);
    }

    [Fact]
    public void RestartRequired_FollowsWhatIsWaiting()
    {
        Compose();
        var viewModel = CreateViewModel();
        viewModel.Load();

        Assert.False(viewModel.RestartRequired);

        _catalog.AddAwaitingRestart(new PluginRecord {
            PluginID = "weather",
            DisplayName = "Weather",
            StagedVersion = "1.1.0",
        });
        viewModel.Load();

        Assert.True(viewModel.RestartRequired);
        Assert.Contains("1.1.0", viewModel.RestartMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void StatCards_CountEachOriginAndTheContractVersion()
    {
        Compose(Module("Notes", "Notes", required: false), Module("Weather", "Weather", required: false));
        AddInstalled("weather", "Weather", nameof(PluginTrustLevel.Unofficial));

        var viewModel = CreateViewModel();
        viewModel.Load();

        var cards = viewModel.StatCards.ToDictionary(card => card.Label, StringComparer.Ordinal);

        Assert.Equal("2", cards["Modules"].Value);
        Assert.Equal("1", cards["Unofficial"].Value);
        Assert.Equal($"v{PluginSdkVersion.Current}", cards["Host SDK"].Value);
    }

    private static CatalogPlugin Offered(string id, string version)
    {
        return new CatalogPlugin(
            id, "Weather", "Tells you the weather.", "Somebody Else",
            version, "1.0", 2 * 1024 * 1024, new string('a', 64), DateTimeOffset.UtcNow);
    }

    private void Offers(params CatalogPlugin[] plugins)
    {
        _catalogService
            .ListAsync(Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<CatalogPlugin>>(plugins));
    }

    [Fact]
    public async Task LoadCatalog_ShowsWhatTheDeploymentOffers()
    {
        Compose();
        Offers(Offered("weather", "1.0.0"));

        var viewModel = CreateViewModel();
        await viewModel.LoadCatalogAsync();

        Assert.Single(viewModel.CatalogItems);
        Assert.Equal(CatalogItemState.Available, viewModel.CatalogItems[0].State);
        Assert.True(viewModel.CatalogItems[0].CanInstall);
    }

    /// <summary>
    /// A row that offers to install what is already here would download it, prompt for it and stage
    /// it over itself — so the state the installation is in decides what the row offers.
    /// </summary>
    [Fact]
    public async Task LoadCatalog_WhatIsAlreadyInstalledIsNotOfferedAgain()
    {
        Compose();
        AddInstalled("weather", "Weather", nameof(PluginTrustLevel.Unofficial));
        Offers(Offered("weather", "1.0.0"));

        var viewModel = CreateViewModel();
        await viewModel.LoadCatalogAsync();

        Assert.Equal(CatalogItemState.Installed, viewModel.CatalogItems[0].State);
        Assert.False(viewModel.CatalogItems[0].CanInstall);
    }

    [Fact]
    public async Task LoadCatalog_ANewerVersionIsAnUpdate()
    {
        Compose();
        AddInstalled("weather", "Weather", nameof(PluginTrustLevel.Unofficial));
        Offers(Offered("weather", "1.1.0"));

        var viewModel = CreateViewModel();
        await viewModel.LoadCatalogAsync();

        Assert.Equal(CatalogItemState.UpdateAvailable, viewModel.CatalogItems[0].State);
        Assert.True(viewModel.CatalogItems[0].CanInstall);
    }

    /// <summary>Downloading the same thing twice before a restart would stage it over itself.</summary>
    [Fact]
    public async Task LoadCatalog_WhatIsAlreadyStagedIsNotOfferedAgain()
    {
        Compose();
        _catalog.AddAwaitingRestart(new PluginRecord {
            PluginID = "weather",
            DisplayName = "Weather",
            StagedVersion = "1.1.0",
        });
        Offers(Offered("weather", "1.1.0"));

        var viewModel = CreateViewModel();
        await viewModel.LoadCatalogAsync();

        Assert.Equal(CatalogItemState.Staged, viewModel.CatalogItems[0].State);
        Assert.False(viewModel.CatalogItems[0].CanInstall);
    }

    [Fact]
    public async Task LoadCatalog_AServerRefusalIsSaidWhereTheListWouldBe()
    {
        Compose();
        _catalogService
            .ListAsync(Arg.Any<CancellationToken>())
            .Returns(Result.Failure<IReadOnlyList<CatalogPlugin>>(
                new Error("Unavailable", "The plugin catalog could not be reached.")));

        var viewModel = CreateViewModel();
        await viewModel.LoadCatalogAsync();

        Assert.Empty(viewModel.CatalogItems);
        Assert.True(viewModel.HasCatalogMessage);
        Assert.Equal("The plugin catalog could not be reached.", viewModel.CatalogMessage);
    }

    [Fact]
    public async Task LoadCatalog_AnEmptyCatalogSaysSoRatherThanShowingNothing()
    {
        Compose();
        Offers();

        var viewModel = CreateViewModel();
        await viewModel.LoadCatalogAsync();

        Assert.True(viewModel.HasCatalogMessage);
    }

    /// <summary>
    /// A download that did not arrive intact must not reach the prompt: the prompt is where a user
    /// decides to run code, and there is nothing there to decide about.
    /// </summary>
    [Fact]
    public async Task PrepareInstallFromCatalog_ARefusedDownloadNeverOpensThePrompt()
    {
        Compose();
        var offered = Offered("weather", "1.0.0");
        _catalogService
            .DownloadAsync(offered, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure(new Error(
                "Plugin.Download.DigestMismatch", "What arrived is not what the catalog published.")));

        var viewModel = CreateViewModel();
        var prepared = await viewModel.PrepareInstallFromCatalogAsync(
            new CatalogListItem(offered, CatalogItemState.Available));

        Assert.False(prepared);
        Assert.Null(viewModel.Pending);
        Assert.True(viewModel.HasError);
    }

    [Fact]
    public void Tab_ShowsExactlyOneOfTheThree()
    {
        Compose();
        var viewModel = CreateViewModel();

        Assert.True(viewModel.ShowingInstalled);

        foreach (var tab in new[] { "Installed", "Browse catalog", "Develop" })
        {
            viewModel.Tab = tab;

            var showing = new[] {
                viewModel.ShowingInstalled, viewModel.ShowingBrowse, viewModel.ShowingDevelop,
            };

            Assert.Single(showing, shown => shown);
        }
    }
}

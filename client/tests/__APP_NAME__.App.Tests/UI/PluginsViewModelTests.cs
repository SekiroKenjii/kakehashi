using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using __ROOT_NAMESPACE__.App.Plugins;
using __ROOT_NAMESPACE__.App.Services;
using __ROOT_NAMESPACE__.App.UI;
using __ROOT_NAMESPACE__.PluginSdk.Abstractions;
using __ROOT_NAMESPACE__.SharedKernel;
using __ROOT_NAMESPACE__.UI.Contracts;
using __ROOT_NAMESPACE__.UI.Contracts.Services.Platform;
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
    private readonly INavigationLayoutService _layout = Substitute.For<INavigationLayoutService>();
    private readonly PluginCatalog _catalog = new();

    public PluginsViewModelTests()
    {
        // The deployment's headings are what a plugin can be filed under. None, unless a test says.
        _layout.Current.Returns(NavigationLayout.None);
    }

    public void Dispose()
    {
        if (System.IO.Directory.Exists(_root))
        {
            System.IO.Directory.Delete(_root, recursive: true);
        }
    }

    private PluginsViewModel CreateViewModel()
    {
        var installer = new PluginInstaller(new PluginPaths(_root), PluginPublisher.Nobody);
        var scaffolder = new PluginScaffolder(_root);

        return new PluginsViewModel(
            _modules, _catalog, installer, _files, _dialogs, scaffolder, _catalogService, _layout);
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

    private void AddInstalled(
        string id, string moduleName, string signature, string version = "1.0.0", string status = "")
    {
        _catalog.Add(new LoadedPlugin(
            new PluginRecord {
                PluginID = id,
                DisplayName = moduleName,
                InstalledVersion = version,
                Signature = signature,
                SignatureStatus = status,
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
    /// Every status is its own news, and they are all Unofficial. Saying "Unsigned" about a package
    /// that carries a real signature is the drift worth catching; a status this build does not
    /// recognise is the one case where the default is right.
    /// </summary>
    [Theory]
    [InlineData("Unsigned", nameof(PluginsViewModel.UnsignedWarning))]
    [InlineData("Valid", nameof(PluginsViewModel.OtherPublisherWarning))]
    [InlineData("Tampered", nameof(PluginsViewModel.TamperedWarning))]
    [InlineData("Expired", nameof(PluginsViewModel.ExpiredWarning))]
    [InlineData("Revoked", nameof(PluginsViewModel.RevokedWarning))]
    [InlineData("Distrusted", nameof(PluginsViewModel.DistrustedWarning))]
    [InlineData("UntrustedRoot", nameof(PluginsViewModel.UntrustedRootWarning))]
    [InlineData("Unknown", nameof(PluginsViewModel.UnsignedWarning))]
    [InlineData("", nameof(PluginsViewModel.UnsignedWarning))]
    public void Load_TheRowSaysWhichKindOfUnofficialItIs(string status, string expected)
    {
        Compose();
        AddInstalled("weather", "Weather", nameof(PluginTrustLevel.Unofficial), status: status);

        var viewModel = CreateViewModel();
        viewModel.Load();

        var warnings = new Dictionary<string, string>(StringComparer.Ordinal) {
            [nameof(PluginsViewModel.UnsignedWarning)] = PluginsViewModel.UnsignedWarning,
            [nameof(PluginsViewModel.OtherPublisherWarning)] = PluginsViewModel.OtherPublisherWarning,
            [nameof(PluginsViewModel.TamperedWarning)] = PluginsViewModel.TamperedWarning,
            [nameof(PluginsViewModel.ExpiredWarning)] = PluginsViewModel.ExpiredWarning,
            [nameof(PluginsViewModel.RevokedWarning)] = PluginsViewModel.RevokedWarning,
            [nameof(PluginsViewModel.DistrustedWarning)] = PluginsViewModel.DistrustedWarning,
            [nameof(PluginsViewModel.UntrustedRootWarning)] = PluginsViewModel.UntrustedRootWarning,
        };

        Assert.Equal(warnings[expected], viewModel.Items[0].Warning);
    }

    /// <summary>
    /// An answer already on the record is an answer. Asking again for bytes that have not changed
    /// teaches people to click through the one prompt that matters.
    /// </summary>
    [Fact]
    public async Task PrepareInstall_DoesNotAskTwiceForTheSameBytes()
    {
        Compose();
        var viewModel = CreateViewModel();
        _files.PickFileAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(WriteUnsignedPackage());
        _ = await viewModel.PrepareInstallFromFileAsync();
        viewModel.ConsentGiven = true;
        _ = await viewModel.ConfirmInstallAsync();
        Promote();

        Assert.True(await viewModel.PrepareInstallFromFileAsync());
        Assert.False(viewModel.ConsentRequired);
        Assert.True(viewModel.CanInstallPending);
    }

    /// <summary>
    /// Reinstalling a version already waiting for the restart it needs is refused rather than
    /// re-staged. Opening one clears the staging directory first, and dismissing the prompt would
    /// take an install already agreed to with it.
    /// </summary>
    [Fact]
    public async Task PrepareInstall_ForAVersionAlreadyStagedIsRefused()
    {
        Compose();
        var viewModel = CreateViewModel();
        _files.PickFileAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(WriteUnsignedPackage());
        _ = await viewModel.PrepareInstallFromFileAsync();
        var staged = viewModel.Pending!.StagedDirectory;
        viewModel.ConsentGiven = true;
        _ = await viewModel.ConfirmInstallAsync();

        Assert.False(await viewModel.PrepareInstallFromFileAsync());
        Assert.True(viewModel.HasError);
        Assert.True(System.IO.Directory.Exists(staged));
    }

    /// <summary>
    /// The file picker is its own window and not modal to this one, so a second Install lands
    /// between the first click and the prompt appearing. The prompt shows whatever is pending, so
    /// it would repaint to describe the second package while somebody is reading the first.
    /// </summary>
    [Fact]
    public async Task PrepareInstall_WhileOneIsAlreadyWaitingIsRefused()
    {
        Compose();
        var viewModel = CreateViewModel();
        _files.PickFileAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(WriteUnsignedPackage());

        Assert.True(await viewModel.PrepareInstallFromFileAsync());
        Assert.False(viewModel.CanStartInstall);
        Assert.False(await viewModel.PrepareInstallFromFileAsync());

        viewModel.CancelInstall();

        Assert.True(viewModel.CanStartInstall);
    }

    /// <summary>What the loader's promotion does to the record, without loading anything.</summary>
    private void Promote()
    {
        var paths = new PluginPaths(_root);
        var state = PluginState.Load(paths);
        var record = state.Find("weather")!;
        record.InstalledVersion = record.StagedVersion;
        record.StagedVersion = string.Empty;
        state.Put(record);

        Assert.True(state.TrySave());
    }

    /// <summary>Turned off, the screen says so rather than showing an empty list.</summary>
    [Fact]
    public void PluginsDisabled_FollowsWhatTheDeploymentDecided()
    {
        Compose();

        Assert.False(CreateViewModel().PluginsDisabled);

        var off = new PluginsViewModel(
            _modules,
            new PluginCatalog { Disabled = true },
            new PluginInstaller(new PluginPaths(_root), PluginPublisher.Nobody),
            _files,
            _dialogs,
            new PluginScaffolder(_root),
            _catalogService,
            _layout);

        Assert.True(off.PluginsDisabled);
    }

    /// <summary>
    /// The whole point of the install prompt: an unverified package cannot be installed without the
    /// user saying so, and the button that would do it is not available until they have.
    /// </summary>
    /// <remarks>
    /// Driven through a real package, because the property is only meaningful once something is
    /// pending — asserting it on an empty view model passes on the "nothing is pending" clause and
    /// proves nothing about the gate.
    /// </remarks>
    [Fact]
    public async Task CanInstallPending_IsFalseUntilAnUnverifiedPackageIsConsentedTo()
    {
        Compose();
        var viewModel = CreateViewModel();
        _files.PickFileAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(WriteUnsignedPackage());

        Assert.True(await viewModel.PrepareInstallFromFileAsync());
        Assert.True(viewModel.ConsentRequired);
        Assert.False(viewModel.CanInstallPending);

        viewModel.ConsentGiven = true;

        Assert.True(viewModel.CanInstallPending);
    }

    /// <summary>
    /// Refusing at the gate must leave the package where it was. Committing without consent throws
    /// the staged files away, so a second attempt would record a directory that is no longer there.
    /// </summary>
    [Fact]
    public async Task ConfirmInstall_WithoutConsent_ChangesNothingAndStagesNothing()
    {
        Compose();
        var viewModel = CreateViewModel();
        _files.PickFileAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(WriteUnsignedPackage());
        _ = await viewModel.PrepareInstallFromFileAsync();
        var staged = viewModel.Pending!.StagedDirectory;

        Assert.False(await viewModel.ConfirmInstallAsync());
        Assert.True(System.IO.Directory.Exists(staged));
        Assert.NotNull(viewModel.Pending);
        Assert.Empty(_catalog.AwaitingRestart);

        viewModel.ConsentGiven = true;

        Assert.True(await viewModel.ConfirmInstallAsync());
        Assert.Single(_catalog.AwaitingRestart);
    }

    /// <summary>The smallest package the installer accepts, signed by nobody.</summary>
    private string WriteUnsignedPackage()
    {
        System.IO.Directory.CreateDirectory(_root);
        var path = System.IO.Path.Combine(_root, "weather" + PluginPackage.Extension);

        using (var file = System.IO.File.Create(path))
        using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
        {
            var manifestEntry = archive.CreateEntry("manifest.json");

            using (var manifest = new StreamWriter(manifestEntry.Open()))
            {
                manifest.Write("""
                    {
                      "schemaVersion": 1,
                      "id": "weather",
                      "moduleName": "Weather",
                      "displayName": "Weather",
                      "version": "1.0.0",
                      "entryAssembly": "App.Modules.Weather.UI.dll",
                      "moduleType": "App.Modules.Weather.UI.WeatherModule",
                      "minHostSdk": "0.1"
                    }
                    """);
            }

            var assemblyEntry = archive.CreateEntry("lib/App.Modules.Weather.UI.dll");

            using var bytes = assemblyEntry.Open();
            bytes.Write([0x4D, 0x5A]);
        }

        return path;
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
        Assert.Contains("on the next launch", viewModel.RestartMessage, StringComparison.Ordinal);
        Assert.Equal("Restart required", viewModel.RestartTitle);
    }

    /// <summary>
    /// Turned off, the loader does not run, so a restart promotes nothing and removes nothing. The
    /// banner said "loads on the next launch" directly under a bar saying it would not.
    /// </summary>
    [Fact]
    public void RestartMessage_WhenPluginsAreOff_SaysWhatIsActuallyWaitedOn()
    {
        Compose();
        var catalog = new PluginCatalog { Disabled = true };
        catalog.AddAwaitingRestart(new PluginRecord {
            PluginID = "weather",
            DisplayName = "Weather",
            StagedVersion = "1.1.0",
        });

        var viewModel = new PluginsViewModel(
            _modules,
            catalog,
            new PluginInstaller(new PluginPaths(_root), PluginPublisher.Nobody),
            _files,
            _dialogs,
            new PluginScaffolder(_root),
            _catalogService,
            _layout);
        viewModel.Load();

        Assert.Contains(
            "when plugins are turned back on", viewModel.RestartMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("next launch", viewModel.RestartMessage, StringComparison.Ordinal);
        Assert.Equal("Waiting for plugins to be turned on", viewModel.RestartTitle);
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

    /// <summary>
    /// The band under the tab strip collapses whole when it holds nothing. A panel that is visible
    /// but empty still contributes its margin, which is the gap this exists to remove.
    /// </summary>
    [Fact]
    public void HasBanner_IsFalseOnlyWhenTheBandWouldBeEmpty()
    {
        Compose();
        var viewModel = CreateViewModel();
        viewModel.Load();

        // The Installed tab always shows the stat cards.
        Assert.True(viewModel.HasBanner);

        viewModel.Tab = "Browse catalog";

        Assert.False(viewModel.HasBanner);

        viewModel.ErrorMessage = "Something to read.";

        Assert.True(viewModel.HasError);
        Assert.True(viewModel.HasBanner);

        viewModel.ErrorMessage = string.Empty;

        Assert.False(viewModel.HasBanner);
    }

    /// <summary>
    /// The headings offered are the deployment's own. A plugin filed under a name no heading has
    /// invents a second heading at the bottom of the pane, so the choice is bounded by what exists.
    /// </summary>
    [Fact]
    public void HeadingChoices_AreTheDeploymentsHeadingsPlusNone()
    {
        Compose();
        _layout.Current.Returns(new NavigationLayout(
            [],
            [new NavigationGroup("Utilities", []), new NavigationGroup("Administration", [])]));

        var choices = CreateViewModel().HeadingChoices;

        Assert.Equal([string.Empty, "Utilities", "Administration"], choices);
    }

    /// <summary>
    /// The heading survives a restart, which means it has to reach the state file — the catalog the
    /// screen reads is a snapshot taken before the container existed.
    /// </summary>
    [Fact]
    public void FileUnder_WritesTheHeadingToTheStateFile()
    {
        Compose(Module("Weather", "Weather", required: false));
        AddInstalled("weather", "Weather", nameof(PluginTrustLevel.Unofficial));
        var paths = new PluginPaths(_root);
        var state = PluginState.Load(paths);
        state.Put(new PluginRecord { PluginID = "weather", InstalledVersion = "1.0.0" });
        Assert.True(state.TrySave());

        var viewModel = CreateViewModel();
        viewModel.Load();
        viewModel.FileUnder(viewModel.Items[0], "Tools");

        var reloaded = PluginState.Load(paths);

        Assert.Equal("Tools", reloaded.Find("weather")!.Group);
    }

    /// <summary>A row that is rebound to a different plugin must not read as somebody choosing.</summary>
    [Fact]
    public void FileUnder_TheHeadingTheRowAlreadyHas_WritesNothing()
    {
        Compose(Module("Weather", "Weather", required: false));
        AddInstalled("weather", "Weather", nameof(PluginTrustLevel.Unofficial));

        var viewModel = CreateViewModel();
        viewModel.Load();
        var row = viewModel.Items[0];

        viewModel.FileUnder(row, row.Group);

        // No state file was ever written, so nothing was recorded and nothing was refused.
        Assert.False(System.IO.File.Exists(new PluginPaths(_root).StateFile));
        Assert.False(viewModel.HasError);
    }

    [Fact]
    public void FileUnder_AnUnknownPluginIsRefusedRatherThanWritten()
    {
        var installer = new PluginInstaller(new PluginPaths(_root), PluginPublisher.Nobody);

        Assert.True(installer.FileUnder("nothing-here", "Tools").IsFailure);
    }

    /// <summary>An installed row carries the heading it is filed under; a built-in has none.</summary>
    [Fact]
    public void Load_AnInstalledRowCarriesItsHeadingAndABuiltInDoesNot()
    {
        Compose(Module("Notes", "Notes", required: false));
        AddInstalled("weather", "Weather", nameof(PluginTrustLevel.Unofficial));
        _catalog.Loaded[0].Record.Group = "Tools";

        var viewModel = CreateViewModel();
        viewModel.Load();

        var installed = viewModel.Items.Single(item => item.Origin == PluginOrigin.Unofficial);
        var builtIn = viewModel.Items.Single(item => item.Origin == PluginOrigin.BuiltIn);

        Assert.Equal("Tools", installed.Group);
        Assert.Equal(string.Empty, builtIn.Group);
        Assert.Empty(builtIn.Headings);
    }
}

using System;
using System.Linq;
using __ROOT_NAMESPACE__.App.Plugins;
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

        return new PluginsViewModel(_modules, _catalog, installer, _files, _dialogs);
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
}

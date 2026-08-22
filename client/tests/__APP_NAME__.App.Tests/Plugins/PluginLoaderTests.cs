using System;
using System.IO;
using System.Linq;
using __ROOT_NAMESPACE__.App.Plugins;
using __ROOT_NAMESPACE__.PluginSdk.Xaml;
using __ROOT_NAMESPACE__.UI.Contracts;
using Xunit;

namespace __ROOT_NAMESPACE__.App.Tests.Plugins;

/// <summary>
/// Unit tests for <see cref="PluginLoader"/>: what it settles before it loads anything, and what it
/// refuses.
/// </summary>
/// <remarks>
/// Loading an assembly needs a real plugin, which is not something this solution has; those cases
/// belong to the fixture plugin the running application is driven against. What is covered here is
/// everything that happens on disk and in the state file — the part that decides which version the
/// next launch sees, and the part that has to be right for an uninstall to actually uninstall.
/// </remarks>
public sealed class PluginLoaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    private PluginPaths Paths => new(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static PluginLoadResult Load(PluginPaths paths)
    {
        return PluginLoader.LoadAll(paths, new PluginXamlHost(), []);
    }

    private static void Save(PluginPaths paths, PluginRecord record)
    {
        var state = PluginState.Load(paths);
        state.Put(record);

        Assert.True(state.TrySave());
    }

    private static void Place(string directory, string content = "{}")
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "manifest.json"), content);
    }

    [Fact]
    public void LoadAll_WithNothingInstalled_LoadsNothingAndFaultsNothing()
    {
        var result = Load(Paths);

        Assert.Empty(result.Modules);
        Assert.Empty(result.Catalog.Faults);
        Assert.False(result.Catalog.RestartRequired);
    }

    [Fact]
    public void LoadAll_PromotesAStagedVersionBeforeLoading()
    {
        var paths = Paths;
        Place(paths.StagedDirectory("weather", "1.1.0"));
        Place(paths.InstalledDirectory("weather", "1.0.0"));
        Save(paths, new PluginRecord {
            PluginID = "weather",
            InstalledVersion = "1.0.0",
            StagedVersion = "1.1.0",
        });

        _ = Load(paths);

        var reloaded = PluginState.Load(paths);
        var promoted = reloaded.Find("weather");

        Assert.NotNull(promoted);
        Assert.Equal("1.1.0", promoted.InstalledVersion);
        Assert.Equal(string.Empty, promoted.StagedVersion);
        Assert.True(Directory.Exists(paths.InstalledDirectory("weather", "1.1.0")));
        Assert.False(Directory.Exists(paths.InstalledDirectory("weather", "1.0.0")));
        Assert.False(Directory.Exists(paths.StagedDirectory("weather", "1.1.0")));
    }

    [Fact]
    public void LoadAll_DeletesWhatAnUninstallMarked()
    {
        var paths = Paths;
        Place(paths.InstalledDirectory("weather", "1.0.0"));
        Save(paths, new PluginRecord {
            PluginID = "weather",
            InstalledVersion = "1.0.0",
            PendingRemove = true,
        });

        var result = Load(paths);

        Assert.Empty(result.Modules);
        Assert.False(Directory.Exists(paths.InstalledRoot("weather")));

        var remaining = PluginState.Load(paths);

        Assert.Null(remaining.Find("weather"));
    }

    [Fact]
    public void LoadAll_StagedVersionThatIsNotOnDisk_IsAFaultAndLeavesTheInstalledOneAlone()
    {
        var paths = Paths;
        Place(paths.InstalledDirectory("weather", "1.0.0"));
        Save(paths, new PluginRecord {
            PluginID = "weather",
            InstalledVersion = "1.0.0",
            StagedVersion = "1.1.0",
        });

        var result = Load(paths);

        Assert.Contains(result.Catalog.Faults, fault => fault.PluginID == "weather");
        Assert.True(Directory.Exists(paths.InstalledDirectory("weather", "1.0.0")));
    }

    [Fact]
    public void LoadAll_InstalledDirectoryThatIsGone_IsAFaultRatherThanAThrow()
    {
        var paths = Paths;
        Save(paths, new PluginRecord {
            PluginID = "weather",
            InstalledVersion = "1.0.0",
        });

        var result = Load(paths);

        Assert.Single(result.Catalog.Faults);
        Assert.Equal("Plugin.Load.DirectoryMissing", result.Catalog.Faults[0].Reason.Code);
    }

    [Fact]
    public void LoadAll_ManifestThatIsNotValid_IsAFault()
    {
        var paths = Paths;
        Place(paths.InstalledDirectory("weather", "1.0.0"), """{ "schemaVersion": 1 }""");
        Save(paths, new PluginRecord {
            PluginID = "weather",
            InstalledVersion = "1.0.0",
        });

        var result = Load(paths);

        Assert.Single(result.Catalog.Faults);
        Assert.Equal("Plugin.Load.Invalid", result.Catalog.Faults[0].Reason.Code);
    }

    [Fact]
    public void PageKeysOf_DropsTheSuffixTheNavigationServiceDrops()
    {
        var items = new[] { new NavigationItem("Weather", "", typeof(WeatherPage)) };

        var keys = PluginLoader.PageKeysOf(items);

        Assert.Equal(["Weather"], keys.ToArray());
    }

    // A stand-in for a page type: the loader only ever reads the name.
    private sealed class WeatherPage
    {
    }
}

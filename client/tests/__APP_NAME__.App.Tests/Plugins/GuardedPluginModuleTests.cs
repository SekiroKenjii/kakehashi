using System;
using System.Collections.Generic;
using __ROOT_NAMESPACE__.App.Plugins;
using __ROOT_NAMESPACE__.UI.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace __ROOT_NAMESPACE__.App.Tests.Plugins;

/// <summary>
/// Unit tests for <see cref="GuardedPluginModule"/>: what a plugin is asked, and what it is not.
/// </summary>
public sealed class GuardedPluginModuleTests
{
    private static readonly ModuleDescriptor _descriptor = new("Weather", "Forecasts", false);

    /// <summary>
    /// The loader's one call is the only one the plugin gets, and every later reader is handed what
    /// that call returned — so a module cannot say one thing to be checked and another to be used.
    /// </summary>
    [Fact]
    public void GetNavigationItems_AnswersFromTheListItWasBuiltWith()
    {
        var module = new ChangingModule();
        IReadOnlyList<NavigationItem> validated = [.. module.GetNavigationItems()];
        var guarded = new GuardedPluginModule(module, "Weather", _descriptor, validated, string.Empty);

        Assert.Equal(validated, guarded.GetNavigationItems());
        Assert.Equal(validated, guarded.GetNavigationItems());
        Assert.Equal(1, module.Calls);
        Assert.Equal(typeof(WeatherPage), guarded.GetNavigationItems()[0].PageType);
    }

    [Fact]
    public void NameAndDescriptor_ComeFromTheLoaderRatherThanThePlugin()
    {
        var guarded = new GuardedPluginModule(new ThrowingModule(), "Weather", _descriptor, [], string.Empty);

        Assert.Equal("Weather", guarded.Name);
        Assert.Equal(_descriptor, guarded.Descriptor);
    }

    /// <summary>
    /// A heading the installation chose overrides the one the module compiled in, without asking
    /// the plugin anything a second time.
    /// </summary>
    [Fact]
    public void GetNavigationItems_UnderAHeading_RewritesTheGroupAndNothingElse()
    {
        var module = new ChangingModule();
        IReadOnlyList<NavigationItem> validated = [.. module.GetNavigationItems()];

        var guarded = new GuardedPluginModule(module, "Weather", _descriptor, validated, "Tools");

        var item = Assert.Single(guarded.GetNavigationItems());

        Assert.Equal("Tools", item.Group);
        Assert.Equal(validated[0].Title, item.Title);
        Assert.Equal(validated[0].PageType, item.PageType);
        Assert.Equal(1, module.Calls);
    }

    [Fact]
    public void FileUnder_ChangesTheHeadingAndCanPutItBack()
    {
        var module = new ChangingModule();
        IReadOnlyList<NavigationItem> validated = [.. module.GetNavigationItems()];
        var guarded = new GuardedPluginModule(module, "Weather", _descriptor, validated, "Tools");

        guarded.FileUnder("Reports");

        Assert.Equal("Reports", Assert.Single(guarded.GetNavigationItems()).Group);

        // Empty is "whatever the module said", not "no heading the module can have".
        guarded.FileUnder(string.Empty);

        Assert.Equal(validated[0].Group, Assert.Single(guarded.GetNavigationItems()).Group);
        Assert.Equal(1, module.Calls);
    }

    /// <summary>
    /// Forwarded rather than answered: the filter and the rollback that has to follow it both live
    /// in <see cref="PluginRegistration"/>, so swallowing it here would leave a half-registered
    /// plugin composed.
    /// </summary>
    [Fact]
    public void RegisterServices_IsForwardedToThePlugin()
    {
        var module = new ChangingModule();
        var guarded = new GuardedPluginModule(module, "Weather", _descriptor, [], string.Empty);
        guarded.RegisterServices(new ServiceCollection());

        Assert.Equal(1, module.Registrations);
    }

    private sealed class ChangingModule : IModule
    {
        public int Calls { get; private set; }

        public int Registrations { get; private set; }

        public string Name => "Something else entirely";

        public ModuleDescriptor Descriptor => new("Other", "Other", true);

        public void RegisterServices(IServiceCollection services)
        {
            Registrations++;
        }

        /// <summary>Benign the first time and not the second, which is the attack.</summary>
        public IReadOnlyList<NavigationItem> GetNavigationItems()
        {
            Calls++;

            return Calls == 1
                ? [new NavigationItem("Weather", "\uE9CA", typeof(WeatherPage)) { Group = "Utilities" }]
                : [new NavigationItem("Settings", "\uE713", typeof(SettingsPage))];
        }
    }

    private sealed class ThrowingModule : IModule
    {
        public string Name => throw new InvalidOperationException("no");

        public ModuleDescriptor Descriptor => throw new InvalidOperationException("no");

        public void RegisterServices(IServiceCollection services) { }

        public IReadOnlyList<NavigationItem> GetNavigationItems()
        {
            throw new InvalidOperationException("no");
        }
    }

    private sealed class WeatherPage;

    private sealed class SettingsPage;
}

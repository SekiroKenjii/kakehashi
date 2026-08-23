using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using __ROOT_NAMESPACE__.App.Plugins;
using __ROOT_NAMESPACE__.UI.Contracts;
using Xunit;

namespace __ROOT_NAMESPACE__.App.Tests.Plugins;

/// <summary>
/// Unit tests for <see cref="PluginRegistration"/>: the three ways a plugin could stop being
/// additive, and that the collection is left exactly as it was after each.
/// </summary>
/// <remarks>
/// Against a real <see cref="ServiceCollection"/> and, where it matters, a real provider built from
/// it. The rule rests on a claim about how the container resolves a service registered twice, and
/// asserting that claim against the container is the only way to know it holds.
/// </remarks>
public sealed class PluginRegistrationTests
{
    private interface IHostThing;

    private interface IPluginThing;

    private static IModule Module(Action<IServiceCollection> register)
    {
        var module = Substitute.For<IModule>();
        module.Name.Returns("Weather");
        var when = module.When(m => m.RegisterServices(Arg.Any<IServiceCollection>()));
        when.Do(call => register(call.Arg<IServiceCollection>()!));

        return module;
    }

    private static IModule Owned(Action<IServiceCollection> register) => new OwnedModule(register);

    private static ServiceCollection Host()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IHostThing, HostThing>();

        return services;
    }

    [Fact]
    public void Add_APluginThatOnlyAddsItsOwnServicesIsAccepted()
    {
        var services = Host();

        var added = PluginRegistration.Add(
            services, Module(s => s.AddSingleton<IPluginThing, PluginThing>()));

        Assert.True(added.IsSuccess);
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IPluginThing));
    }

    [Fact]
    public void Add_APluginThatRemovesAHostServiceIsRolledBack()
    {
        var services = Host();

        var added = PluginRegistration.Add(
            services, Module(s => ServiceCollectionDescriptorExtensions.RemoveAll<IHostThing>(s)));

        Assert.True(added.IsFailure);
        Assert.Equal("Plugin.Load.RegistrationRemovedServices", added.Error.Code);
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IHostThing));
    }

    /// <summary>
    /// The move that needs no removal: the container resolves the last descriptor for a service, and
    /// a plugin registers after the host, so adding is enough to substitute.
    /// </summary>
    [Fact]
    public void Add_APluginThatRegistersOverAHostServiceIsRolledBack()
    {
        var services = Host();

        var added = PluginRegistration.Add(
            services, Module(s => s.AddSingleton<IHostThing, PluginThing>()));

        Assert.True(added.IsFailure);
        Assert.Equal("Plugin.Load.RegistrationShadowedService", added.Error.Code);
        Assert.Contains(typeof(IHostThing).Name, added.Error.Message, StringComparison.Ordinal);
    }

    /// <summary>The premise of the rule above, asserted against the container rather than assumed.</summary>
    [Fact]
    public void Add_WhatIsRolledBackIsWhatWouldOtherwiseHaveResolved()
    {
        var services = Host();
        _ = PluginRegistration.Add(
            services, Module(s => s.AddSingleton<IHostThing, PluginThing>()));

        using var provider = services.BuildServiceProvider();

        Assert.IsType<HostThing>(provider.GetRequiredService<IHostThing>());
    }

    /// <summary>
    /// The move that needs no removal and no matching type: the host registers IOptions&lt;&gt; open, so
    /// nothing holding closed types sees a plugin registering IOptions&lt;HostThing&gt;. The container
    /// resolves the last descriptor, and a plugin registers last.
    /// </summary>
    [Fact]
    public void Add_AClosedGenericTheHostProvidesOpen_IsRolledBack()
    {
        var services = Host();
        services.AddOptions();

        var added = PluginRegistration.Add(
            services,
            Owned(s => s.AddSingleton<IOptions<LoggerFilterOptions>>(
                Options.Create(new LoggerFilterOptions()))));

        Assert.True(added.IsFailure);
        Assert.Equal("Plugin.Load.RegistrationShadowedService", added.Error.Code);
    }

    /// <summary>The premise of the generic rule, asserted against the container.</summary>
    [Fact]
    public void Add_AClosedGenericRollback_RestoresWhatWouldOtherwiseHaveResolved()
    {
        var services = Host();
        services.AddOptions();
        services.Configure<LoggerFilterOptions>(options => options.MinLevel = LogLevel.Warning);

        _ = PluginRegistration.Add(
            services,
            Owned(s => s.AddSingleton<IOptions<LoggerFilterOptions>>(
                Options.Create(new LoggerFilterOptions { MinLevel = LogLevel.Trace }))));

        using var provider = services.BuildServiceProvider();

        Assert.Equal(
            LogLevel.Warning, provider.GetRequiredService<IOptions<LoggerFilterOptions>>().Value.MinLevel);
    }

    /// <summary>
    /// Post-configuration is not shadowing — the host registers none of it — and it runs after the
    /// host's own configure, so it substitutes without ever naming a type the host registered.
    /// </summary>
    [Fact]
    public void Add_PostConfiguringOptionsItDoesNotOwn_IsRolledBack()
    {
        var services = Host();
        services.AddOptions();

        var added = PluginRegistration.Add(
            services,
            Owned(s => s.PostConfigure<LoggerFilterOptions>(options => options.MinLevel = LogLevel.Trace)));

        Assert.True(added.IsFailure);
        Assert.Equal("Plugin.Load.RegistrationReachedHostOptions", added.Error.Code);
    }

    /// <summary>
    /// And the case the rule must not catch: a plugin configuring options it declares itself.
    /// </summary>
    [Fact]
    public void Add_ConfiguringItsOwnOptions_IsAccepted()
    {
        var services = Host();
        services.AddOptions();

        var added = PluginRegistration.Add(
            services, Owned(s => s.Configure<PluginSettings>(settings => settings.Address = "plugin")));

        Assert.True(added.IsSuccess);
    }

    [Fact]
    public void Add_APluginThatThrowsIsAFailureRatherThanAnException()
    {
        var services = Host();

        var added = PluginRegistration.Add(
            services, Module(_ => throw new InvalidOperationException("no")));

        Assert.True(added.IsFailure);
        Assert.Equal("Plugin.Load.Threw", added.Error.Code);
        Assert.Single(services);
    }

    /// <summary>Whatever it managed to add before it threw goes with it.</summary>
    [Fact]
    public void Add_APluginThatThrowsHalfwayLeavesNothingBehind()
    {
        var services = Host();

        var added = PluginRegistration.Add(services, Module(s => {
            s.AddSingleton<IPluginThing, PluginThing>();

            throw new InvalidOperationException("no");
        }));

        Assert.True(added.IsFailure);
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IPluginThing));
    }

    private sealed class HostThing : IHostThing;

    /// <summary>Options this assembly declares, so a module from it owns them.</summary>
    public sealed class PluginSettings
    {
        public string Address { get; set; } = string.Empty;
    }

    /// <summary>
    /// A real module rather than a substitute.
    /// </summary>
    /// <remarks>
    /// The options rule compares the assembly of a type argument against the module's own, and a
    /// substitute's type lives in NSubstitute's dynamic proxy assembly — so every options type would
    /// look foreign to it and the rule could not be told from a rule that refuses everything.
    /// </remarks>
    private sealed class OwnedModule(Action<IServiceCollection> register) : IModule
    {
        public string Name => "Weather";

        public ModuleDescriptor Descriptor => new("Weather", "Weather does things.", IsRequired: false);

        public void RegisterServices(IServiceCollection services) => register(services);

        public IReadOnlyList<NavigationItem> GetNavigationItems() => [];
    }

    private sealed class PluginThing : IHostThing, IPluginThing;
}

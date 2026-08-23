using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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

    private sealed class PluginThing : IHostThing, IPluginThing;
}

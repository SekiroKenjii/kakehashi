using System;
using System.Collections.Generic;
using System.Reflection;
using __ROOT_NAMESPACE__.UI.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace __ROOT_NAMESPACE__.App.Plugins;

/// <summary>
/// The plugin's module, answering with what the loader validated.
/// </summary>
/// <remarks>
/// A plugin is asked for its name, its descriptor and its navigation items once, inside the
/// loader's filters, and every later reader is given those answers rather than the plugin. Two
/// things follow, and both are the point.
/// <para>
/// A plugin cannot answer differently the second time. The loader checks the page keys against what
/// <see cref="IModule.GetNavigationItems"/> returned, and the navigation service registers from a
/// second call — so without this, a module could return one list to be checked and another to be
/// registered, and take a screen the check had refused.
/// </para>
/// <para>
/// And a plugin that throws from one of them is a row with a reason rather than an application that
/// will not start. The loader's filter is the only place these are called.
/// </para>
/// <para>
/// <see cref="RegisterServices"/> is forwarded rather than answered, because
/// <see cref="PluginRegistration"/> owns both that filter and the rollback that has to follow it.
/// </para>
/// </remarks>
public sealed class GuardedPluginModule(
    IModule module,
    string name,
    ModuleDescriptor descriptor,
    IReadOnlyList<NavigationItem> navigationItems) : IModule
{
    /// <summary>
    /// The assembly the plugin's own code came from.
    /// </summary>
    /// <remarks>
    /// Stated rather than reflected on this instance: composition asks whose code a registration
    /// belongs to, and reflecting on the guard answers with the host's, which inverts the question.
    /// </remarks>
    public Assembly Assembly => module.GetType().Assembly;

    public string Name => name;

    public ModuleDescriptor Descriptor => descriptor;

    public void RegisterServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        module.RegisterServices(services);
    }

    public IReadOnlyList<NavigationItem> GetNavigationItems() => navigationItems;
}

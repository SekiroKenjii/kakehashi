using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;

namespace __ROOT_NAMESPACE__.UI.Contracts;

/// <summary>
/// A self-contained feature module. The host discovers modules, lets each register its own
/// services, and surfaces the navigation entries they contribute. Modules never reference one
/// another directly; they communicate through events on the mediator.
/// </summary>
public interface IModule
{
    /// <summary>A stable, human-readable module name used in diagnostics.</summary>
    string Name { get; }

    /// <summary>Presentation metadata: display name, description, and whether it is required.</summary>
    ModuleDescriptor Descriptor { get; }

    /// <summary>Registers the module's application, domain and infrastructure services.</summary>
    void RegisterServices(IServiceCollection services);

    /// <summary>The navigation entries this module contributes to the shell.</summary>
    IReadOnlyList<NavigationItem> GetNavigationItems();
}

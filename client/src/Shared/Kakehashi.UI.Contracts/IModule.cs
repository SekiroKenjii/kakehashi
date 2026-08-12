using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;

namespace Kakehashi.UI.Contracts {
  // Modules never reference one another directly; they communicate through events on the mediator.
  public interface IModule {
    // Stable identifier rather than a label: the rest of the host keys off it.
    string Name { get; }

    ModuleDescriptor Descriptor { get; }

    void RegisterServices(IServiceCollection services);

    IReadOnlyList<NavigationItem> GetNavigationItems();
  }
}

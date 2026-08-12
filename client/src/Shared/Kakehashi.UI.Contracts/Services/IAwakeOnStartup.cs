using System;

namespace Kakehashi.UI.Contracts.Services {
  // For work that has to happen early but is not a step in the startup sequence: a service whose
  // point is the wiring it does on the way up rather than anything a caller asks it for.
  public interface IAwakeOnStartup : ISingletonDependency {
    string Name { get; }

    // Called once, after construction, before any orchestrator runs.
    void Initialize(IServiceProvider serviceProvider);
  }
}

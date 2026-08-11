using System;

namespace Kakehashi.UI.Contracts.Services {
  /// <summary>
  /// A service the composition root builds and initialises before any orchestrator runs.
  /// </summary>
  /// <remarks>
  /// For work that has to happen early but is not a step in the startup sequence — a service whose
  /// point is the wiring it does on the way up rather than anything a caller asks it for.
  /// </remarks>
  public interface IAwakeOnStartup : ISingletonDependency {
    /// <summary>What to call this one in startup logs.</summary>
    string Name { get; }

    /// <summary>Called once, after construction, before the orchestrators.</summary>
    /// <param name="serviceProvider">The container, for the dependencies this resolves itself.</param>
    void Initialize(IServiceProvider serviceProvider);
  }
}

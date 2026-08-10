using System;

namespace Kakehashi.UI.Contracts.Services {
  /// <summary>
  /// Services that implement this interface will be created during app startup before any of the
  /// orchestrators run. This is useful for services that need to "awake" (do some work) during
  /// startup but don't fit well as an orchestrator themselves, for example because they need to be
  /// created early or because their behavior is more of a side effect of their creation rather than
  /// a sequence of steps to run.
  /// </summary>
  public interface IAwakeOnStartup : ISingletonDependency {
    /// <summary>
    /// A name to identify the service in logs and telemetry during startup. This is useful because all services that implement this interface will be created during startup and it can be hard to tell them apart in logs and telemetry without a name.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Initializes the service. This is called by the composition root when the app starts up; the service instance is created and then this method is called on it. This is a bit unusual but it works and keeps the service reusable without forcing a specific DI container or composition approach on the app.
    /// </summary>
    /// <param name="serviceProvider"></param>
    void Initialize(IServiceProvider serviceProvider);
  }
}

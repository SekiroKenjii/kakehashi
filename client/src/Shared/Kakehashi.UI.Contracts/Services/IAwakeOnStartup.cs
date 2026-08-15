using System;

namespace Kakehashi.UI.Contracts.Services;

/// <summary>
/// A service the composition root constructs and initializes during startup, before the
/// orchestrators run — for work that is a side effect of the service existing rather than an
/// orchestration step.
/// </summary>
public interface IAwakeOnStartup : ISingletonDependency
{
    /// <summary>Identifies the service in startup logs and telemetry.</summary>
    string Name { get; }

    /// <summary>
    /// Called once by the composition root, after construction and before the orchestrators run.
    /// </summary>
    void Initialize(IServiceProvider serviceProvider);
}

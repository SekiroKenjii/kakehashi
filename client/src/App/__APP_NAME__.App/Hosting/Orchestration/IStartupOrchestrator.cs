using System.Threading;
using System.Threading.Tasks;

namespace __ROOT_NAMESPACE__.App.Hosting.Orchestration;

/// <summary>
/// One ordered step in the application's startup pipeline. Implementations are discovered from the
/// container and run in ascending <see cref="Order"/> by the <see cref="AppOrchestrator"/>.
/// </summary>
public interface IStartupOrchestrator
{
    /// <summary>
    /// Relative execution order; lower values run first. Every value lives in
    /// <see cref="StartupOrder"/>, never as a literal here.
    /// </summary>
    int Order { get; }

    /// <summary>Name used for logging and diagnostics.</summary>
    string Name { get; }

    /// <summary>Short user-facing text shown on the splash screen while this step runs.</summary>
    string Description { get; }

    Task ExecuteAsync(CancellationToken cancellationToken);
}

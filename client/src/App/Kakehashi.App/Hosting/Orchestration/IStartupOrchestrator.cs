using System.Threading;
using System.Threading.Tasks;

namespace Kakehashi.App.Hosting.Orchestration {
  /// <summary>
  /// One ordered step in the application's startup pipeline. Implementations are discovered from the
  /// container and run in ascending <see cref="Order"/> by the <see cref="AppOrchestrator"/>.
  /// </summary>
  public interface IStartupOrchestrator {
    /// <summary>Relative execution order; lower values run first.</summary>
    int Order { get; }

    /// <summary>
    /// A descriptive name for this startup step, used for logging and diagnostics.
    /// </summary>
    string Name { get; }

    /// <summary>Short user-facing text shown on the splash screen while this step runs.</summary>
    string Description { get; }

    /// <summary>Runs this startup step.</summary>
    Task ExecuteAsync(CancellationToken cancellationToken);
  }
}

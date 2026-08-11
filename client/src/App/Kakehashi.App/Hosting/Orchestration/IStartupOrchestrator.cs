using System.Threading;
using System.Threading.Tasks;

namespace Kakehashi.App.Hosting.Orchestration {
  // One ordered step in the application's startup pipeline. Implementations are discovered from the
  // container and run in ascending Order by the AppOrchestrator.
  public interface IStartupOrchestrator {
    // Relative execution order; lower values run first.
    int Order { get; }

    // A descriptive name for this startup step, used for logging and diagnostics.
    string Name { get; }

    // Short user-facing text shown on the splash screen while this step runs.
    string Description { get; }

    // Runs this startup step.
    Task ExecuteAsync(CancellationToken cancellationToken);
  }
}

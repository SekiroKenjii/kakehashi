using System.Threading;
using System.Threading.Tasks;

namespace Kakehashi.App.Hosting.Orchestration {
  // Implementations are discovered from the container and run by AppOrchestrator.
  public interface IStartupOrchestrator {
    // Lower values run first.
    int Order { get; }

    string Name { get; }

    // Shown on the splash while this step runs.
    string Description { get; }

    Task ExecuteAsync(CancellationToken cancellationToken);
  }
}

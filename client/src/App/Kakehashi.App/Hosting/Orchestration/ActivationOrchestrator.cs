using System.Threading;
using System.Threading.Tasks;

namespace Kakehashi.App.Hosting.Orchestration {
  // Activates the main window and dismisses the splash. Runs last.
  public sealed class ActivationOrchestrator : IStartupOrchestrator {
    private readonly StartupContext _context;

    public ActivationOrchestrator(StartupContext context) {
      _context = context;
    }

    public int Order => 40;

    public string Name => nameof(ActivationOrchestrator);

    public string Description => "Ready";

    public Task ExecuteAsync(CancellationToken cancellationToken) {
      _context.MainWindow?.Activate();
      _context.Splash?.Close();
      _context.Splash = null;
      return Task.CompletedTask;
    }
  }
}

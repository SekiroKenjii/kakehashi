using System.Threading;
using System.Threading.Tasks;

namespace Kakehashi.App.Hosting.Orchestration {
  // Order 40: last, so the splash is dismissed only once every other step has run.
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

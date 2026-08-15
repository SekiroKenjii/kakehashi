using System.Threading;
using System.Threading.Tasks;

namespace __ROOT_NAMESPACE__.App.Hosting.Orchestration;

/// <summary>Activates the main window and dismisses the splash. Runs last.</summary>
public sealed class ActivationOrchestrator : IStartupOrchestrator
{
    private readonly StartupContext _context;

    public ActivationOrchestrator(StartupContext context)
    {
        _context = context;
    }

    public int Order => 40;

    public string Name => nameof(ActivationOrchestrator);

    public string Description => "Ready";

    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        _context.MainWindow?.Activate();
        _context.Splash?.Close();
        _context.Splash = null;

        return Task.CompletedTask;
    }
}

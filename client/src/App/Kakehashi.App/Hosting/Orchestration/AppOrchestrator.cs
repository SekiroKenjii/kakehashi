using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kakehashi.App.Infrastructure.Observability;
using Kakehashi.UI.Contracts.Services;
using Microsoft.Extensions.Logging;

namespace Kakehashi.App.Hosting.Orchestration {
  // Coordinates application startup by running the registered IStartupOrchestrators in
  // order. Keeping this out of App.xaml.cs keeps the composition root lean and makes the
  // startup sequence easy to extend (add an orchestrator) and to reason about.
  public sealed partial class AppOrchestrator {
    private readonly IReadOnlyList<IStartupOrchestrator> _orchestrators;
    private readonly StartupContext _context;
    private readonly ILogger<AppOrchestrator> _logger;

    public AppOrchestrator(
        IEnumerable<IStartupOrchestrator> orchestrators,
        StartupContext context,
        ILogger<AppOrchestrator> logger) {
      ArgumentNullException.ThrowIfNull(orchestrators);
      ArgumentNullException.ThrowIfNull(context);
      ArgumentNullException.ThrowIfNull(logger);

      _orchestrators = [.. orchestrators.OrderBy(orchestrator => orchestrator.Order)];
      _context = context;
      _logger = logger;
    }

    public async Task StartAsync(IEnumerable<IAwakeOnStartup> awakeOnStartupServices, CancellationToken cancellationToken = default) {
      using var activity = Telemetry.ActivitySource.StartActivity("App.Startup");

      foreach (var service in awakeOnStartupServices) {
        cancellationToken.ThrowIfCancellationRequested();
        activity?.AddTag("awakening.service", service.GetType().FullName);
        service.Initialize(App.Services);
      }

      for (int index = 0; index < _orchestrators.Count; index++) {
        var orchestrator = _orchestrators[index];
        cancellationToken.ThrowIfCancellationRequested();
        activity?.AddTag("orchestrator.name", orchestrator.Name);
        LogRunningOrchestrator(orchestrator.Name);

        // The splash is created by the first orchestrator, so earlier steps have nowhere to report.
        _context.Splash?.ViewModel.ReportProgress(
            index + 1, _orchestrators.Count, orchestrator.Description);

        await orchestrator.ExecuteAsync(cancellationToken);
      }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Running startup orchestrator {OrchestratorName}.")]
    private partial void LogRunningOrchestrator(string orchestratorName);
  }
}

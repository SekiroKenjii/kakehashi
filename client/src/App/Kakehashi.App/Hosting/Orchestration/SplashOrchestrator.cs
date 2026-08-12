using System;
using System.Threading;
using System.Threading.Tasks;
using Kakehashi.App.UI;
using Microsoft.Extensions.DependencyInjection;

namespace Kakehashi.App.Hosting.Orchestration {
  public sealed class SplashOrchestrator : IStartupOrchestrator {
    private readonly IServiceProvider _services;
    private readonly StartupContext _context;

    public SplashOrchestrator(IServiceProvider services, StartupContext context) {
      _services = services;
      _context = context;
    }

    public int Order => 10;

    public string Name => nameof(SplashOrchestrator);

    public string Description => "Initializing...";

    public async Task ExecuteAsync(CancellationToken cancellationToken) {
      var splash = _services.GetRequiredService<SplashWindow>();
      _context.Splash = splash;
      splash.Activate();

      // A stand-in for real warm-up (priming caches, the backend channel), still to be written. The
      // delay also keeps the splash visible on fast machines.
      await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
    }
  }
}

using System;
using System.Threading;
using System.Threading.Tasks;
using __ROOT_NAMESPACE__.App.UI;
using Microsoft.Extensions.DependencyInjection;

namespace __ROOT_NAMESPACE__.App.Hosting.Orchestration;

/// <summary>Shows the splash window first and performs any startup warm-up.</summary>
public sealed class SplashOrchestrator : IStartupOrchestrator
{
    private readonly IServiceProvider _services;
    private readonly StartupContext _context;

    public SplashOrchestrator(IServiceProvider services, StartupContext context)
    {
        _services = services;
        _context = context;
    }

    public int Order => StartupOrder.Splash;

    public string Name => nameof(SplashOrchestrator);

    public string Description => "Initializing...";

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var splash = _services.GetRequiredService<SplashWindow>();
        _context.Splash = splash;
        splash.Activate();

        // Stand-in for real warm-up (priming caches, the backend channel, etc.). The brief delay also
        // ensures the splash is visible even on fast machines; replace with genuine startup work.
        await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
    }
}

using System;
using System.Threading;
using System.Threading.Tasks;
using __ROOT_NAMESPACE__.App.UI;
using Microsoft.Extensions.DependencyInjection;

namespace __ROOT_NAMESPACE__.App.Hosting.Orchestration;

/// <summary>Creates the main window, hosts the shell inside it, and wires the custom title bar.</summary>
public sealed class ShellOrchestrator : IStartupOrchestrator
{
    private readonly IServiceProvider _services;
    private readonly StartupContext _context;

    public ShellOrchestrator(IServiceProvider services, StartupContext context)
    {
        _services = services;
        _context = context;
    }

    public int Order => StartupOrder.Shell;

    public string Name => nameof(ShellOrchestrator);

    public string Description => "Preparing workspace...";

    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var window = _services.GetRequiredService<MainWindow>();
        var shell = _services.GetRequiredService<ShellPage>();

        window.AttachShell(shell);
        App.Current.SetMainWindow(window);
        _context.MainWindow = window;
        App.AppTitleBar = shell.TitleBar;

        return Task.CompletedTask;
    }
}

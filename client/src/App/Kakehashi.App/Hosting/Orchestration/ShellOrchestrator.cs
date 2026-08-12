using System;
using System.Threading;
using System.Threading.Tasks;
using Kakehashi.App.UI;
using Microsoft.Extensions.DependencyInjection;

namespace Kakehashi.App.Hosting.Orchestration {
  public sealed class ShellOrchestrator : IStartupOrchestrator {
    private readonly IServiceProvider _services;
    private readonly StartupContext _context;

    public ShellOrchestrator(IServiceProvider services, StartupContext context) {
      _services = services;
      _context = context;
    }

    public int Order => 20;

    public string Name => nameof(ShellOrchestrator);

    public string Description => "Preparing workspace...";

    public Task ExecuteAsync(CancellationToken cancellationToken) {
      var window = _services.GetRequiredService<MainWindow>();
      var shell = _services.GetRequiredService<ShellPage>();

      window.AttachShell(shell);
      App.Current.SetMainWindow(window);
      _context.MainWindow = window;
      App.AppTitleBar = shell.TitleBar;

      return Task.CompletedTask;
    }
  }
}

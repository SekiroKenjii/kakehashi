using System;
using System.Threading;
using System.Threading.Tasks;
using Kakehashi.UI.Contracts.Services.Platform;

namespace Kakehashi.App.Hosting.Orchestration {
  // Order 30: after the shell (20), because the theme is applied to the main window's content.
  public sealed class ThemeOrchestrator : IStartupOrchestrator {
    private readonly IThemeService _themeService;

    public ThemeOrchestrator(IThemeService themeService) {
      ArgumentNullException.ThrowIfNull(themeService);
      _themeService = themeService;
    }

    public int Order => 30;

    public string Name => nameof(ThemeOrchestrator);

    public string Description => "Applying theme...";

    public Task ExecuteAsync(CancellationToken cancellationToken) {
      _themeService.Initialize();
      return Task.CompletedTask;
    }
  }
}

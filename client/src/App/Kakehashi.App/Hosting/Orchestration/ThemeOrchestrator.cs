using System;
using System.Threading;
using System.Threading.Tasks;
using Kakehashi.UI.Contracts.Services.Platform;

namespace Kakehashi.App.Hosting.Orchestration;

/// <summary>Applies the persisted theme once the main window content exists.</summary>
public sealed class ThemeOrchestrator : IStartupOrchestrator
{
    private readonly IThemeService _themeService;

    public ThemeOrchestrator(IThemeService themeService)
    {
        ArgumentNullException.ThrowIfNull(themeService);
        _themeService = themeService;
    }

    public int Order => 30;

    public string Name => nameof(ThemeOrchestrator);

    public string Description => "Applying theme...";

    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        _themeService.Initialize();
        return Task.CompletedTask;
    }
}

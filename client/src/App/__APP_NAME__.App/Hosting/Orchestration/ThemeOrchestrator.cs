using System;
using System.Threading;
using System.Threading.Tasks;
using __ROOT_NAMESPACE__.UI.Contracts.Services.Platform;

namespace __ROOT_NAMESPACE__.App.Hosting.Orchestration;

/// <summary>Applies the persisted theme and accent once the main window content exists.</summary>
public sealed class ThemeOrchestrator : IStartupOrchestrator
{
    private readonly IThemeService _themeService;
    private readonly IAccentService _accentService;

    public ThemeOrchestrator(IThemeService themeService, IAccentService accentService)
    {
        ArgumentNullException.ThrowIfNull(themeService);
        ArgumentNullException.ThrowIfNull(accentService);
        _themeService = themeService;
        _accentService = accentService;
    }

    public int Order => 30;

    public string Name => nameof(ThemeOrchestrator);

    public string Description => "Applying theme...";

    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        _themeService.Initialize();
        // After the theme: the accent's re-resolve nudge flips the root's requested theme away
        // and back, so the theme has to be the settled one first.
        _accentService.Initialize();

        return Task.CompletedTask;
    }
}

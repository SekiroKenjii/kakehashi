using System;
using System.Threading;
using System.Threading.Tasks;
using __ROOT_NAMESPACE__.UI.Contracts.Services.Platform;

namespace __ROOT_NAMESPACE__.App.Hosting.Orchestration;

/// <summary>
/// Applies the persisted accent before any window can resolve an accent brush. The framework binds
/// those brushes the first time a window draws, and reads only what is in place by then — which is
/// why this runs ahead of the splash rather than beside the theme.
/// </summary>
public sealed class AccentOrchestrator : IStartupOrchestrator
{
    private readonly IAccentService _accentService;

    public AccentOrchestrator(IAccentService accentService)
    {
        ArgumentNullException.ThrowIfNull(accentService);
        _accentService = accentService;
    }

    public int Order => 5;

    public string Name => nameof(AccentOrchestrator);

    public string Description => "Applying accent...";

    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        _accentService.Initialize();

        return Task.CompletedTask;
    }
}

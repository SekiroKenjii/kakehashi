using Kakehashi.App.UI;

namespace Kakehashi.App.Hosting.Orchestration;

/// <summary>
/// Mutable state shared across the startup orchestrators — the windows they create and hand off to
/// one another (e.g. the splash created early and dismissed once the shell is ready).
/// </summary>
public sealed class StartupContext
{
    public SplashWindow? Splash { get; set; }

    public MainWindow? MainWindow { get; set; }
}

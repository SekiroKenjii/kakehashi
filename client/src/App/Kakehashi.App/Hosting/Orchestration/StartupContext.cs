using Kakehashi.App.UI;

namespace Kakehashi.App.Hosting.Orchestration {
  /// <summary>
  /// Mutable state shared across the startup orchestrators — the windows they create and hand off to
  /// one another (e.g. the splash created early and dismissed once the shell is ready).
  /// </summary>
  public sealed class StartupContext {
    /// <summary>The splash window shown during startup, if any.</summary>
    public SplashWindow? Splash { get; set; }

    /// <summary>The main application window, once created.</summary>
    public MainWindow? MainWindow { get; set; }
  }
}

using Kakehashi.App.UI;

namespace Kakehashi.App.Hosting.Orchestration {
  // Mutable state shared across the startup orchestrators — the windows they create and hand off to
  // one another (e.g. the splash created early and dismissed once the shell is ready).
  public sealed class StartupContext {
    // The splash window shown during startup, if any.
    public SplashWindow? Splash { get; set; }

    // The main application window, once created.
    public MainWindow? MainWindow { get; set; }
  }
}

using Kakehashi.App.UI;

namespace Kakehashi.App.Hosting.Orchestration {
  // The windows the startup orchestrators create and hand off to one another: the splash is created
  // by one step and dismissed by another, once the shell is ready.
  public sealed class StartupContext {
    public SplashWindow? Splash { get; set; }

    public MainWindow? MainWindow { get; set; }
  }
}

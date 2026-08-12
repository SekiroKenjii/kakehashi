using System.Diagnostics;

namespace Kakehashi.Modules.Auth.UI {
  // Nothing is exported unless AddObservability calls .AddSource(SourceName).
  public static class AuthTelemetry {
    public const string SourceName = "Kakehashi.Modules.Auth";

    public static readonly ActivitySource Source = new(SourceName);
  }
}

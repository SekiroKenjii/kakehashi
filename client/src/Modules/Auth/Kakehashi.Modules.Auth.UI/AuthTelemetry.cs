using System.Diagnostics;

namespace Kakehashi.Modules.Auth.UI {
  // The Auth module's tracing source. Register it for export in AddObservability with
  // .AddSource("Kakehashi.Modules.Auth").
  public static class AuthTelemetry {
    public const string SourceName = "Kakehashi.Modules.Auth";

    public static readonly ActivitySource Source = new(SourceName);
  }
}

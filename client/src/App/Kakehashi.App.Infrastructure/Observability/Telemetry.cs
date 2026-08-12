using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;

namespace Kakehashi.App.Infrastructure.Observability {
  // The ActivitySource and Meter are registered with OpenTelemetry by name in AddObservability.
  public static class Telemetry {
    public static readonly string ServiceName =
        typeof(Telemetry).Assembly.GetName().Name ?? "Kakehashi.App";

    public static readonly string ServiceVersion =
        typeof(Telemetry).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(Telemetry).Assembly.GetName().Version?.ToString()
        ?? "0.0.0";

    public static readonly ActivitySource ActivitySource = new(ServiceName, ServiceVersion);

    public static readonly Meter Meter = new(ServiceName, ServiceVersion);
  }
}

using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;

namespace Kakehashi.App.Infrastructure.Observability {
  // The application's single ActivitySource and Meter. Use
  // ActivitySource to create custom spans and Meter to create custom
  // instruments; both are registered with OpenTelemetry by name in
  // ObservabilityServiceCollectionExtensions.AddObservability.
  public static class Telemetry {
    // Logical service name, also used as the activity-source and meter name.
    public static readonly string ServiceName =
        typeof(Telemetry).Assembly.GetName().Name ?? "Kakehashi.App";

    // The assembly's informational/file version, reported as the OpenTelemetry service version.
    public static readonly string ServiceVersion =
        typeof(Telemetry).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(Telemetry).Assembly.GetName().Version?.ToString()
        ?? "0.0.0";

    // The source for application-authored traces (spans).
    public static readonly ActivitySource ActivitySource = new(ServiceName, ServiceVersion);

    // The meter for application-authored metrics.
    public static readonly Meter Meter = new(ServiceName, ServiceVersion);
  }
}

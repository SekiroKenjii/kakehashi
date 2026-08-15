using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;

namespace __ROOT_NAMESPACE__.App.Infrastructure.Observability;

/// <summary>
/// The application's single <see cref="ActivitySource"/> and <see cref="Meter"/>. Use
/// <see cref="ActivitySource"/> to create custom spans and <see cref="Meter"/> to create custom
/// instruments; both are registered with OpenTelemetry by name in
/// <c>ObservabilityServiceCollectionExtensions.AddObservability</c>.
/// </summary>
public static class Telemetry
{
    /// <summary>Logical service name, also used as the activity-source and meter name.</summary>
    public static readonly string ServiceName =
        typeof(Telemetry).Assembly.GetName().Name ?? "__APP_NAME__.App";

    /// <summary>The assembly's informational/file version, reported as the OpenTelemetry service version.</summary>
    public static readonly string ServiceVersion =
        typeof(Telemetry).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(Telemetry).Assembly.GetName().Version?.ToString()
        ?? "0.0.0";

    public static readonly ActivitySource ActivitySource = new(ServiceName, ServiceVersion);

    public static readonly Meter Meter = new(ServiceName, ServiceVersion);
}

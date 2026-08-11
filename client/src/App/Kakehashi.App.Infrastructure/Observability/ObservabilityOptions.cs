namespace Kakehashi.App.Infrastructure.Observability {
  // Strongly-typed configuration for OpenTelemetry, bound from the Observability section of
  // appsettings.json. Exporters are opt-in so a developer with no collector running sees no
  // noise; turn on EnableConsoleExporter for local debugging or
  // EnableOtlpExporter to ship to a collector / Aspire dashboard / Jaeger.
  public sealed class ObservabilityOptions {
    // The configuration section these options bind to.
    public const string SectionName = "Observability";

    // Service name reported on every signal. Defaults to the infrastructure assembly name.
    public string ServiceName { get; set; } = "Kakehashi.App";

    // Emit distributed traces.
    public bool EnableTracing { get; set; } = true;

    // Emit metrics (process/runtime + HTTP client).
    public bool EnableMetrics { get; set; } = true;

    // Route ILogger logs through the OpenTelemetry logging pipeline.
    public bool EnableLogging { get; set; } = true;

    // Write traces/metrics/logs to stdout (handy for local dotnet run).
    public bool EnableConsoleExporter { get; set; }

    // Export traces/metrics/logs over OTLP to OtlpEndpoint.
    public bool EnableOtlpExporter { get; set; }

    // OTLP collector endpoint (e.g. http://localhost:4317). Empty uses the SDK default.
    public string? OtlpEndpoint { get; set; }
  }
}

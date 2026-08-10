namespace Kakehashi.App.Infrastructure.Observability {
  /// <summary>
  /// Strongly-typed configuration for OpenTelemetry, bound from the <c>Observability</c> section of
  /// <c>appsettings.json</c>. Exporters are opt-in so a developer with no collector running sees no
  /// noise; turn on <see cref="EnableConsoleExporter"/> for local debugging or
  /// <see cref="EnableOtlpExporter"/> to ship to a collector / Aspire dashboard / Jaeger.
  /// </summary>
  public sealed class ObservabilityOptions {
    /// <summary>The configuration section these options bind to.</summary>
    public const string SectionName = "Observability";

    /// <summary>Service name reported on every signal. Defaults to the infrastructure assembly name.</summary>
    public string ServiceName { get; set; } = "Kakehashi.App";

    /// <summary>Emit distributed traces.</summary>
    public bool EnableTracing { get; set; } = true;

    /// <summary>Emit metrics (process/runtime + HTTP client).</summary>
    public bool EnableMetrics { get; set; } = true;

    /// <summary>Route <c>ILogger</c> logs through the OpenTelemetry logging pipeline.</summary>
    public bool EnableLogging { get; set; } = true;

    /// <summary>Write traces/metrics/logs to stdout (handy for local <c>dotnet run</c>).</summary>
    public bool EnableConsoleExporter { get; set; }

    /// <summary>Export traces/metrics/logs over OTLP to <see cref="OtlpEndpoint"/>.</summary>
    public bool EnableOtlpExporter { get; set; }

    /// <summary>OTLP collector endpoint (e.g. <c>http://localhost:4317</c>). Empty uses the SDK default.</summary>
    public string? OtlpEndpoint { get; set; }
  }
}

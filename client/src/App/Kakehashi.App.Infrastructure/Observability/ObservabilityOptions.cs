namespace Kakehashi.App.Infrastructure.Observability {
  /// <summary>
  /// Strongly-typed configuration for OpenTelemetry, bound from the <c>Observability</c> section of
  /// <c>appsettings.json</c>. Exporters are opt-in so a developer with no collector running sees no
  /// noise; turn on <see cref="EnableConsoleExporter"/> for local debugging or
  /// <see cref="EnableOtlpExporter"/> to ship to a collector / Aspire dashboard / Jaeger.
  /// </summary>
  public sealed class ObservabilityOptions {
    public const string SectionName = "Observability";

    public string ServiceName { get; set; } = "Kakehashi.App";

    public bool EnableTracing { get; set; } = true;

    public bool EnableMetrics { get; set; } = true;

    public bool EnableLogging { get; set; } = true;

    public bool EnableConsoleExporter { get; set; }

    public bool EnableOtlpExporter { get; set; }

    /// <summary>Empty uses the SDK's default OTLP endpoint.</summary>
    public string? OtlpEndpoint { get; set; }
  }
}

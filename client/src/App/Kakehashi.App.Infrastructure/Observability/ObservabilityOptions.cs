namespace Kakehashi.App.Infrastructure.Observability {
  // Exporters are opt-in so a developer with no collector running sees no noise.
  public sealed class ObservabilityOptions {
    public const string SectionName = "Observability";

    public string ServiceName { get; set; } = "Kakehashi.App";

    public bool EnableTracing { get; set; } = true;

    public bool EnableMetrics { get; set; } = true;

    public bool EnableLogging { get; set; } = true;

    public bool EnableConsoleExporter { get; set; }

    public bool EnableOtlpExporter { get; set; }

    // Empty uses the SDK default endpoint.
    public string? OtlpEndpoint { get; set; }
  }
}

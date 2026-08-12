using System;
using Kakehashi.App.Infrastructure.Backend;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Kakehashi.App.Infrastructure.Observability {
  public static class ObservabilityServiceCollectionExtensions {
    public static IServiceCollection AddObservability(
        this IServiceCollection services, IConfiguration configuration) {
      ArgumentNullException.ThrowIfNull(services);
      ArgumentNullException.ThrowIfNull(configuration);

      var section = configuration.GetSection(ObservabilityOptions.SectionName);
      services.Configure<ObservabilityOptions>(section);
      var options = section.Get<ObservabilityOptions>() ?? new ObservabilityOptions();

      var resource = ResourceBuilder.CreateDefault().AddService(
          serviceName: options.ServiceName,
          serviceVersion: Telemetry.ServiceVersion,
          serviceInstanceId: Environment.MachineName);

      var builder = services.AddOpenTelemetry().ConfigureResource(r => r.AddService(
          serviceName: options.ServiceName,
          serviceVersion: Telemetry.ServiceVersion,
          serviceInstanceId: Environment.MachineName));

      if (options.EnableTracing) {
        builder.WithTracing(tracing => {
          // Grpc.Net.Client runs over HttpClient, so HTTP-client instrumentation already captures
          // gRPC calls as spans. The dedicated OpenTelemetry.Instrumentation.GrpcNetClient package
          // (gRPC-semantic attributes) is prerelease-only today; add it when it ships stable.
          tracing
              .AddSource(Telemetry.ServiceName)
              // Harmless when the Auth module is absent — no spans are emitted.
              .AddSource("Kakehashi.Modules.Auth")
              .AddHttpClientInstrumentation();

          if (options.EnableConsoleExporter) {
            tracing.AddConsoleExporter();
          }
          if (options.EnableOtlpExporter) {
            tracing.AddOtlpExporter(otlp => ApplyOtlpEndpoint(otlp, options.OtlpEndpoint));
          }
        });
      }

      if (options.EnableMetrics) {
        builder.WithMetrics(metrics => {
          metrics
              .AddMeter(Telemetry.ServiceName)
              .AddHttpClientInstrumentation()
              .AddRuntimeInstrumentation();

          if (options.EnableConsoleExporter) {
            metrics.AddConsoleExporter();
          }
          if (options.EnableOtlpExporter) {
            metrics.AddOtlpExporter(otlp => ApplyOtlpEndpoint(otlp, options.OtlpEndpoint));
          }
        });
      }

      if (options.EnableLogging) {
        services.AddLogging(logging => logging.AddOpenTelemetry(otel => {
          otel.SetResourceBuilder(resource);
          otel.IncludeFormattedMessage = true;
          otel.IncludeScopes = true;

          if (options.EnableConsoleExporter) {
            otel.AddConsoleExporter();
          }
          if (options.EnableOtlpExporter) {
            otel.AddOtlpExporter(otlp => ApplyOtlpEndpoint(otlp, options.OtlpEndpoint));
          }
        }));
      }

      return services;
    }

    private static void ApplyOtlpEndpoint(
        OpenTelemetry.Exporter.OtlpExporterOptions otlp, string? endpoint) {
      if (!string.IsNullOrWhiteSpace(endpoint)) {
        otlp.Endpoint = new Uri(endpoint);
      }
    }
  }
}

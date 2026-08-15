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

namespace Kakehashi.App.Infrastructure.Observability;

/// <summary>
/// Registers OpenTelemetry tracing, metrics and logging. Tracing follows the app's
/// <see cref="Telemetry.ActivitySource"/> plus outbound HTTP and gRPC client calls; metrics include
/// process/runtime and HTTP-client instruments; logs flow through the OpenTelemetry logging provider.
/// Each signal and exporter is gated by <see cref="ObservabilityOptions"/> so it stays quiet until
/// configured.
/// </summary>
public static class ObservabilityServiceCollectionExtensions
{
    public static IServiceCollection AddObservability(
        this IServiceCollection services, IConfiguration configuration)
    {
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

        if (options.EnableTracing)
        {
            builder.WithTracing(tracing => {
                // Grpc.Net.Client runs over HttpClient, so HTTP-client instrumentation already captures
                // gRPC calls as spans; the gRPC-semantic package is prerelease-only today.
                tracing
                    .AddSource(Telemetry.ServiceName)
                    // The Auth module's ActivitySource; harmless when the module is absent (no spans emitted).
                    .AddSource("Kakehashi.Modules.Auth")
                    .AddHttpClientInstrumentation();

                if (options.EnableConsoleExporter)
                {
                    tracing.AddConsoleExporter();
                }
                if (options.EnableOtlpExporter)
                {
                    tracing.AddOtlpExporter(otlp => ApplyOtlpEndpoint(otlp, options.OtlpEndpoint));
                }
            });
        }

        if (options.EnableMetrics)
        {
            builder.WithMetrics(metrics => {
                metrics
                    .AddMeter(Telemetry.ServiceName)
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();

                if (options.EnableConsoleExporter)
                {
                    metrics.AddConsoleExporter();
                }
                if (options.EnableOtlpExporter)
                {
                    metrics.AddOtlpExporter(otlp => ApplyOtlpEndpoint(otlp, options.OtlpEndpoint));
                }
            });
        }

        if (options.EnableLogging)
        {
            services.AddLogging(logging => logging.AddOpenTelemetry(otel => {
                otel.SetResourceBuilder(resource);
                otel.IncludeFormattedMessage = true;
                otel.IncludeScopes = true;

                if (options.EnableConsoleExporter)
                {
                    otel.AddConsoleExporter();
                }
                if (options.EnableOtlpExporter)
                {
                    otel.AddOtlpExporter(otlp => ApplyOtlpEndpoint(otlp, options.OtlpEndpoint));
                }
            }));
        }

        return services;
    }

    private static void ApplyOtlpEndpoint(
        OpenTelemetry.Exporter.OtlpExporterOptions otlp, string? endpoint)
    {
        if (!string.IsNullOrWhiteSpace(endpoint))
        {
            otlp.Endpoint = new Uri(endpoint);
        }
    }
}

using System.Collections.Generic;
using Kakehashi.App.Infrastructure.Observability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Trace;
using Xunit;

namespace Kakehashi.App.Infrastructure.Tests;

public sealed class ObservabilityTests
{
    [Fact]
    public void Binds_observability_options_from_configuration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> {
                ["Observability:ServiceName"] = "Test.Service",
                ["Observability:EnableOtlpExporter"] = "true",
                ["Observability:OtlpEndpoint"] = "http://localhost:4317",
            })
            .Build();

        var options =
            configuration
                .GetSection(ObservabilityOptions.SectionName)
                .Get<ObservabilityOptions>();

        Assert.NotNull(options);
        Assert.Equal("Test.Service", options.ServiceName);
        Assert.True(options.EnableOtlpExporter);
        Assert.Equal("http://localhost:4317", options.OtlpEndpoint);
    }

    [Fact]
    public void AddObservability_registers_the_tracer_provider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        var services = new ServiceCollection();

        services.AddObservability(configuration);

        using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetService<TracerProvider>());
    }
}

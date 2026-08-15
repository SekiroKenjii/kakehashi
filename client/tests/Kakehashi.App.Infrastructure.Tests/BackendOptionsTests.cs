using System.Collections.Generic;
using Kakehashi.App.Infrastructure.Backend;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Kakehashi.App.Infrastructure.Tests;

public sealed class BackendOptionsTests
{
    [Fact]
    public void Binds_all_values_from_configuration()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?> {
            ["Backend:BaseAddress"] = "https://api.example.com",
            ["Backend:Protocol"] = "Grpc",
            ["Backend:TimeoutSeconds"] = "45",
        });

        var options = configuration
            .GetSection(BackendOptions.SectionName)
            .Get<BackendOptions>();

        Assert.NotNull(options);
        Assert.Equal("https://api.example.com", options.BaseAddress);
        Assert.Equal(BackendProtocol.Grpc, options.Protocol);
        Assert.Equal(45, options.TimeoutSeconds);
    }

    [Fact]
    public void Defaults_to_http_when_protocol_absent()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?> {
            ["Backend:BaseAddress"] = "https://api.example.com",
        });

        var options = configuration
            .GetSection(BackendOptions.SectionName)
            .Get<BackendOptions>()
            ?? new BackendOptions();

        Assert.Equal(BackendProtocol.Http, options.Protocol);
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}

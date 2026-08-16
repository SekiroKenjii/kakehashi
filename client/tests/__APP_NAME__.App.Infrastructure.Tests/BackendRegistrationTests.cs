using System;
using System.Collections.Generic;
using System.Linq;
using __ROOT_NAMESPACE__.App.Infrastructure.Backend.Contracts;
using __ROOT_NAMESPACE__.App.Infrastructure.Backend.Grpc;
using __ROOT_NAMESPACE__.App.Infrastructure.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace __ROOT_NAMESPACE__.App.Infrastructure.Tests;

public sealed class BackendRegistrationTests
{
    [Fact]
    public void Http_protocol_registers_a_backend_client()
    {
        var services = AddBackend("Http");

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IBackendClient));
    }

    [Fact]
    public void Grpc_protocol_registers_the_grpc_backend_client()
    {
        var services = AddBackend("Grpc");

        var descriptor = services.Single(d => d.ServiceType == typeof(IBackendClient));
        Type? implementationType = descriptor.ImplementationType;

        Assert.Equal(typeof(GrpcBackendClient), implementationType);
    }

    [Fact]
    public void Http_protocol_does_not_register_the_grpc_client()
    {
        var services = AddBackend("Http");

        var descriptor = services.Single(d => d.ServiceType == typeof(IBackendClient));
        Type? implementationType = descriptor.ImplementationType;

        Assert.NotEqual(typeof(GrpcBackendClient), implementationType);
    }

    private static IServiceCollection AddBackend(string protocol)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> {
                ["Backend:BaseAddress"] = "https://localhost:5001",
                ["Backend:Protocol"] = protocol,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddBackendInfrastructure(configuration);

        return services;
    }
}

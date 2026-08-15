using System;
using Kakehashi.App.Infrastructure.Backend;
using Kakehashi.App.Infrastructure.Backend.Contracts;
using Kakehashi.App.Infrastructure.Backend.Grpc;
using Kakehashi.App.Infrastructure.Backend.Http;
using Kakehashi.Application.Abstractions.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using HealthV1 = Kakehashi.Health.V1;

namespace Kakehashi.App.Infrastructure.DependencyInjection;

/// <summary>
/// Composition entry point for the host-side infrastructure: the backend client (HTTP or gRPC,
/// selected by configuration) and the outbound bearer-token attachment. Observability is registered
/// separately via <c>AddObservability</c> so the transport instrumentation it adds is wired
/// regardless of protocol.
/// </summary>
public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddBackendInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(BackendOptions.SectionName);
        services.Configure<BackendOptions>(section);
        var options = section.Get<BackendOptions>() ?? new BackendOptions();

        // Outbound bearer-token attachment. The default provider returns no token (so the backend
        // works unauthenticated); the Auth module replaces it with a session-backed provider.
        services.TryAddSingleton<IAccessTokenProvider, NullAccessTokenProvider>();
        services.AddTransient<BearerTokenHandler>();

        switch (options.Protocol)
        {
            case BackendProtocol.Grpc:
                services.AddBackendGrpcClient<HealthV1.HealthService.HealthServiceClient>();
                services.AddTransient<IBackendClient, GrpcBackendClient>();
                break;

            case BackendProtocol.Http:
            default:
                services
                    .AddHttpClient<IBackendClient, HttpBackendClient>(client => {
                        client.BaseAddress = new Uri(options.BaseAddress);
                        client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
                    })
                    .AddHttpMessageHandler<BearerTokenHandler>();
                break;
        }

        return services;
    }

    /// <summary>
    /// Registers a generated gRPC client pointed at the backend, with the session access token
    /// attached to every call.
    /// </summary>
    /// <remarks>
    /// The single registration path for generated gRPC clients: feature modules call it from
    /// <c>IModule.RegisterServices</c> rather than assembling a channel, so every client carries
    /// the bearer token, and the resolve-time address read makes registration order irrelevant.
    /// See docs/adr/0009-centralized-grpc-client-registration.md
    /// </remarks>
    public static IHttpClientBuilder AddBackendGrpcClient<TClient>(this IServiceCollection services)
        where TClient : class
    {
        ArgumentNullException.ThrowIfNull(services);

        return services
            .AddGrpcClient<TClient>((provider, client) => {
                var options = provider.GetRequiredService<IOptions<BackendOptions>>().Value;
                client.Address = new Uri(options.BaseAddress);
            })
            // Call credentials normally require a secured channel, and development reaches the backend
            // over plain HTTP; in production TLS is terminated by the reverse proxy in front of it.
            .ConfigureChannel(channel => channel.UnsafeUseInsecureChannelCallCredentials = true)
            .AddCallCredentials(async (context, metadata, serviceProvider) => {
                var tokenProvider = serviceProvider.GetRequiredService<IAccessTokenProvider>();
                var token = await tokenProvider
              .GetAccessTokenAsync(context.CancellationToken)
              .ConfigureAwait(false);

                if (!string.IsNullOrEmpty(token))
                {
                    metadata.Add("Authorization", $"Bearer {token}");
                }
            });
    }
}

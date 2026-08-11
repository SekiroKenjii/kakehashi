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

namespace Kakehashi.App.Infrastructure.DependencyInjection {
  // Composition entry point for the host-side infrastructure: the backend client (HTTP or gRPC,
  // selected by configuration) and the outbound bearer-token attachment. Observability is registered
  // separately via AddObservability so the transport instrumentation it adds is wired
  // regardless of protocol.
  public static class InfrastructureServiceCollectionExtensions {
    public static IServiceCollection AddBackendInfrastructure(
        this IServiceCollection services, IConfiguration configuration) {
      ArgumentNullException.ThrowIfNull(services);
      ArgumentNullException.ThrowIfNull(configuration);

      var section = configuration.GetSection(BackendOptions.SectionName);
      services.Configure<BackendOptions>(section);
      var options = section.Get<BackendOptions>() ?? new BackendOptions();

      // Outbound bearer-token attachment. The default provider returns no token (so the backend
      // works unauthenticated); the Auth module replaces it with a session-backed provider.
      services.TryAddSingleton<IAccessTokenProvider, NullAccessTokenProvider>();
      services.AddTransient<BearerTokenHandler>();

      switch (options.Protocol) {
        case BackendProtocol.Grpc:
          services.AddBackendGrpcClient<HealthV1.HealthService.HealthServiceClient>();
          services.AddTransient<IBackendClient, GrpcBackendClient>();
          break;

        case BackendProtocol.Http:
        default:
          services.AddHttpClient<IBackendClient, HttpBackendClient>(client => {
            client.BaseAddress = new Uri(options.BaseAddress);
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
          }).AddHttpMessageHandler<BearerTokenHandler>();
          break;
      }

      return services;
    }

    // Registers a generated gRPC client pointed at the backend, with the session access token
    // attached to every call.
    //
    // Feature modules call this from IModule.RegisterServices rather than assembling a
    // channel themselves. The token plumbing is the reason: a module that wired its own client
    // and forgot the call credentials would send unauthenticated requests, and it would keep
    // working right up until the server started checking. One place to get it right, and no way
    // to half-do it.
    //
    // The address is read at resolve time rather than captured at registration, so a module can
    // register before or after AddBackendInfrastructure without the order mattering.
    public static IHttpClientBuilder AddBackendGrpcClient<TClient>(this IServiceCollection services)
        where TClient : class {
      ArgumentNullException.ThrowIfNull(services);

      return services
          .AddGrpcClient<TClient>((provider, client) => {
            var options = provider.GetRequiredService<IOptions<BackendOptions>>().Value;
            client.Address = new Uri(options.BaseAddress);
          })
          // Call credentials normally require a secured channel. In development the backend is
          // reached over plain HTTP behind nothing, so the guard has to be lifted or no token is
          // ever sent; in production TLS is terminated by the reverse proxy in front of it.
          .ConfigureChannel(channel => channel.UnsafeUseInsecureChannelCallCredentials = true)
          .AddCallCredentials(async (context, metadata, serviceProvider) => {
            var tokenProvider = serviceProvider.GetRequiredService<IAccessTokenProvider>();
            var token = await tokenProvider
                .GetAccessTokenAsync(context.CancellationToken)
                .ConfigureAwait(false);
            if (!string.IsNullOrEmpty(token)) {
              metadata.Add("Authorization", $"Bearer {token}");
            }
          });
    }
  }
}

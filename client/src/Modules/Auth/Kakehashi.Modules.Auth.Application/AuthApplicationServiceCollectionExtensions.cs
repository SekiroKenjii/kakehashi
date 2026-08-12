using System;
using System.Reflection;
using Kakehashi.Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace Kakehashi.Modules.Auth.Application {
  public static class AuthApplicationServiceCollectionExtensions {
    public static Assembly Assembly => typeof(AuthApplicationServiceCollectionExtensions).Assembly;

    public static IServiceCollection AddAuthApplication(this IServiceCollection services) {
      ArgumentNullException.ThrowIfNull(services);
      services.AddMediator(Assembly);
      return services;
    }
  }
}

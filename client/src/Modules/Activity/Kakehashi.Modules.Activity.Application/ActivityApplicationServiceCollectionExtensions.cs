using System;
using System.Reflection;
using Kakehashi.Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace Kakehashi.Modules.Activity.Application {
  public static class ActivityApplicationServiceCollectionExtensions {
    public static Assembly Assembly =>
        typeof(ActivityApplicationServiceCollectionExtensions).Assembly;

    public static IServiceCollection AddActivityApplication(this IServiceCollection services) {
      ArgumentNullException.ThrowIfNull(services);
      services.AddMediator(Assembly);
      return services;
    }
  }
}

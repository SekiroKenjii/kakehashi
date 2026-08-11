using System;
using System.Reflection;
using Kakehashi.Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace Kakehashi.Modules.Activity.Application {
  // Registers the Activity application layer: its message handlers.
  public static class ActivityApplicationServiceCollectionExtensions {
    // The assembly that contains the Activity application handlers.
    public static Assembly Assembly =>
        typeof(ActivityApplicationServiceCollectionExtensions).Assembly;

    public static IServiceCollection AddActivityApplication(this IServiceCollection services) {
      ArgumentNullException.ThrowIfNull(services);
      services.AddMediator(Assembly);
      return services;
    }
  }
}

using System;
using System.Reflection;
using __ROOT_NAMESPACE__.Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace __ROOT_NAMESPACE__.Modules.Activity.Application;

/// <summary>Registers the Activity application layer: its message handlers.</summary>
public static class ActivityApplicationServiceCollectionExtensions
{
    /// <summary>The assembly that contains the Activity application handlers.</summary>
    public static Assembly Assembly =>
        typeof(ActivityApplicationServiceCollectionExtensions).Assembly;

    public static IServiceCollection AddActivityApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddMediator(Assembly);

        return services;
    }
}

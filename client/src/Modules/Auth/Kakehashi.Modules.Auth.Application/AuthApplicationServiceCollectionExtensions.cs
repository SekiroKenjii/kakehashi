using System;
using System.Reflection;
using Kakehashi.Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace Kakehashi.Modules.Auth.Application;

/// <summary>Registers the Auth application layer: its message handlers.</summary>
public static class AuthApplicationServiceCollectionExtensions
{
    /// <summary>The assembly that contains the Auth application handlers.</summary>
    public static Assembly Assembly => typeof(AuthApplicationServiceCollectionExtensions).Assembly;

    public static IServiceCollection AddAuthApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddMediator(Assembly);
        return services;
    }
}

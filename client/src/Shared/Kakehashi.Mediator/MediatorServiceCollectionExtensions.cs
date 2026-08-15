using System;
using System.Linq;
using System.Reflection;
using Kakehashi.Application.Abstractions;
using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.Mediator.Behaviors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kakehashi.Mediator;

/// <summary>Registers the mediator and scans assemblies for message handlers.</summary>
public static class MediatorServiceCollectionExtensions
{
    private static readonly Type[] _handlerInterfaces = {
  typeof(IRequestHandler<,>),
  typeof(INotificationHandler<>),
  typeof(IDomainEventHandler<>),
};

    /// <summary>
    /// Registers the mediator core services and every request, notification and domain-event
    /// handler discovered in <paramref name="handlerAssemblies"/>.
    /// </summary>
    public static IServiceCollection AddMediator(
        this IServiceCollection services,
        Assembly firstHandlerAssembly,
        params ReadOnlySpan<Assembly> handlerAssemblies)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(firstHandlerAssembly);

        services.TryAddTransient<IMediator, Mediator>();
        services.TryAddTransient<ISender>(provider => provider.GetRequiredService<IMediator>());
        services.TryAddTransient<IPublisher>(provider => provider.GetRequiredService<IMediator>());
        services.TryAddTransient<IDomainEventDispatcher, DomainEventDispatcher>();
        services.TryAddSingleton<IClock, SystemClock>();
        services.TryAddEnumerable(
            ServiceDescriptor.Transient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>)));

        RegisterHandlers(services, firstHandlerAssembly);
        foreach (var assembly in handlerAssemblies)
        {
            RegisterHandlers(services, assembly);
        }

        return services;
    }

    private static void RegisterHandlers(IServiceCollection services, Assembly assembly)
    {
        var implementations = assembly
            .GetTypes()
            .Where(type => type is { IsAbstract: false, IsInterface: false, IsGenericTypeDefinition: false });
        foreach (var implementation in implementations)
        {
            var handlerServices = implementation
                .GetInterfaces()
                .Where(@interface => @interface.IsGenericType
                    && _handlerInterfaces.Contains(@interface.GetGenericTypeDefinition()));
            foreach (var handlerService in handlerServices)
            {
                services.AddTransient(handlerService, implementation);
            }
        }
    }
}

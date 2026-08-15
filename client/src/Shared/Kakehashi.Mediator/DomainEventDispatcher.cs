using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.SharedKernel;

namespace Kakehashi.Mediator;

/// <summary>Dispatches domain events to every registered <see cref="IDomainEventHandler{T}"/>.</summary>
public sealed class DomainEventDispatcher : IDomainEventDispatcher
{
    private static readonly ConcurrentDictionary<Type, DomainEventHandlerWrapper> _wrappers = new();

    private readonly IServiceProvider _services;

    public DomainEventDispatcher(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
    }

    public async Task DispatchAsync(
        IReadOnlyCollection<IDomainEvent> domainEvents, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvents);
        foreach (var domainEvent in domainEvents)
        {
            var wrapper = _wrappers.GetOrAdd(
                domainEvent.GetType(),
                eventType => {
                    var wrapperType = typeof(DomainEventHandlerWrapperImpl<>).MakeGenericType(eventType);

                    return (DomainEventHandlerWrapper)Activator.CreateInstance(wrapperType)!;
                });
            await wrapper.Handle(domainEvent, _services, cancellationToken);
        }
    }
}

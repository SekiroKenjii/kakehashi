using System;
using System.Threading;
using System.Threading.Tasks;
using __ROOT_NAMESPACE__.Application.Abstractions.Messaging;
using __ROOT_NAMESPACE__.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace __ROOT_NAMESPACE__.Mediator;

internal abstract class DomainEventHandlerWrapper
{
    public abstract Task Handle(
        IDomainEvent domainEvent, IServiceProvider services, CancellationToken cancellationToken);
}

internal sealed class DomainEventHandlerWrapperImpl<TDomainEvent> : DomainEventHandlerWrapper
    where TDomainEvent : IDomainEvent
{
    public override async Task Handle(
        IDomainEvent domainEvent, IServiceProvider services, CancellationToken cancellationToken)
    {
        var handlers = services.GetServices<IDomainEventHandler<TDomainEvent>>();
        foreach (var handler in handlers)
        {
            await handler.Handle((TDomainEvent)domainEvent, cancellationToken);
        }
    }
}

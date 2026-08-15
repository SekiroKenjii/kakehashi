using System;
using System.Threading;
using System.Threading.Tasks;
using __ROOT_NAMESPACE__.Application.Abstractions.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace __ROOT_NAMESPACE__.Mediator;

internal abstract class NotificationHandlerWrapper
{
    public abstract Task Handle(
        INotification notification, IServiceProvider services, CancellationToken cancellationToken);
}

internal sealed class NotificationHandlerWrapperImpl<TNotification> : NotificationHandlerWrapper
    where TNotification : INotification
{
    public override async Task Handle(
        INotification notification, IServiceProvider services, CancellationToken cancellationToken)
    {
        var handlers = services.GetServices<INotificationHandler<TNotification>>();
        foreach (var handler in handlers)
        {
            await handler.Handle((TNotification)notification, cancellationToken);
        }
    }
}

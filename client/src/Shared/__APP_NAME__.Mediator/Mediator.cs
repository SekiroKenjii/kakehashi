using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using __ROOT_NAMESPACE__.Application.Abstractions.Messaging;

namespace __ROOT_NAMESPACE__.Mediator;

/// <summary>
/// Default in-process <see cref="IMediator"/>. Resolves handlers and pipeline behaviors from the
/// container on demand and caches the closed-generic dispatch wrappers per message type.
/// </summary>
public sealed class Mediator : IMediator
{
    private static readonly ConcurrentDictionary<Type, RequestHandlerWrapperBase> _requestWrappers =
        new();
    private static readonly ConcurrentDictionary<Type, NotificationHandlerWrapper>
        _notificationWrappers = new();

    private readonly IServiceProvider _services;

    public Mediator(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
    }

    public Task<TResponse> Send<TResponse>(
        IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var wrapper = (RequestHandlerWrapper<TResponse>)_requestWrappers.GetOrAdd(
            request.GetType(),
            requestType => {
                var wrapperType = typeof(RequestHandlerWrapperImpl<,>)
                    .MakeGenericType(requestType, typeof(TResponse));

                return (RequestHandlerWrapperBase)Activator.CreateInstance(wrapperType)!;
            });

        return wrapper.Handle(request, _services, cancellationToken);
    }

    public Task Publish(INotification notification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);
        var wrapper = _notificationWrappers.GetOrAdd(
            notification.GetType(),
            notificationType => {
                var wrapperType = typeof(NotificationHandlerWrapperImpl<>)
                    .MakeGenericType(notificationType);

                return (NotificationHandlerWrapper)Activator.CreateInstance(wrapperType)!;
            });

        return wrapper.Handle(notification, _services, cancellationToken);
    }
}

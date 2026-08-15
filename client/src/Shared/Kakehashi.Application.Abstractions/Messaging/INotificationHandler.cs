using System.Threading;
using System.Threading.Tasks;

namespace Kakehashi.Application.Abstractions.Messaging;

/// <summary>Handles a published <typeparamref name="TNotification"/>.</summary>
/// <typeparam name="TNotification">The notification type.</typeparam>
public interface INotificationHandler<in TNotification>
    where TNotification : INotification
{
    /// <summary>Handles one published notification.</summary>
    Task Handle(TNotification notification, CancellationToken cancellationToken);
}

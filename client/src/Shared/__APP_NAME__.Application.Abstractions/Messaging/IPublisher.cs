using System.Threading;
using System.Threading.Tasks;

namespace __ROOT_NAMESPACE__.Application.Abstractions.Messaging;

/// <summary>Publishes a notification to all of its handlers.</summary>
public interface IPublisher
{
    /// <summary>Publishes to every registered handler.</summary>
    Task Publish(INotification notification, CancellationToken cancellationToken = default);
}

using System.Threading;
using System.Threading.Tasks;

namespace Kakehashi.Application.Abstractions.Messaging {
  /// <summary>Publishes a notification to all of its handlers.</summary>
  public interface IPublisher {
    Task Publish(INotification notification, CancellationToken cancellationToken = default);
  }
}

using System.Threading;
using System.Threading.Tasks;

namespace Kakehashi.Application.Abstractions.Messaging {
  public interface IPublisher {
    Task Publish(INotification notification, CancellationToken cancellationToken = default);
  }
}

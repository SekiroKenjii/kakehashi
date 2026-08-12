using System.Threading;
using System.Threading.Tasks;

namespace Kakehashi.Application.Abstractions.Messaging {
  public interface INotificationHandler<in TNotification>
      where TNotification : INotification {
    Task Handle(TNotification notification, CancellationToken cancellationToken);
  }
}

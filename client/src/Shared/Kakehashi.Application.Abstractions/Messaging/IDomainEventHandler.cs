using System.Threading;
using System.Threading.Tasks;
using Kakehashi.SharedKernel;

namespace Kakehashi.Application.Abstractions.Messaging {
  public interface IDomainEventHandler<in TDomainEvent>
      where TDomainEvent : IDomainEvent {
    Task Handle(TDomainEvent domainEvent, CancellationToken cancellationToken);
  }
}

using System.Threading;
using System.Threading.Tasks;
using Kakehashi.SharedKernel;

namespace Kakehashi.Application.Abstractions.Messaging {
  /// <summary>Reacts to a <typeparamref name="TDomainEvent"/> raised within the same module.</summary>
  /// <typeparam name="TDomainEvent">The domain event type.</typeparam>
  public interface IDomainEventHandler<in TDomainEvent>
      where TDomainEvent : IDomainEvent {
    Task Handle(TDomainEvent domainEvent, CancellationToken cancellationToken);
  }
}

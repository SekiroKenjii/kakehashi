using System.Threading;
using System.Threading.Tasks;
using __ROOT_NAMESPACE__.SharedKernel;

namespace __ROOT_NAMESPACE__.Application.Abstractions.Messaging;

/// <summary>Reacts to a <typeparamref name="TDomainEvent"/> raised within the same module.</summary>
/// <typeparam name="TDomainEvent">The domain event type.</typeparam>
public interface IDomainEventHandler<in TDomainEvent>
    where TDomainEvent : IDomainEvent
{
    /// <summary>Handles one raised event.</summary>
    Task Handle(TDomainEvent domainEvent, CancellationToken cancellationToken);
}

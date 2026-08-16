using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using __ROOT_NAMESPACE__.SharedKernel;

namespace __ROOT_NAMESPACE__.Application.Abstractions.Messaging;

/// <summary>Publishes domain events collected from aggregates to their handlers.</summary>
public interface IDomainEventDispatcher
{
    /// <summary>Dispatches each event to its handlers, in the order given.</summary>
    Task DispatchAsync(
        IReadOnlyCollection<IDomainEvent> domainEvents, CancellationToken cancellationToken);
}

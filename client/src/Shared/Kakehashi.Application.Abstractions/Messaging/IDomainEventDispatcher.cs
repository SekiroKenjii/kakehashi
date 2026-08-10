using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kakehashi.SharedKernel;

namespace Kakehashi.Application.Abstractions.Messaging {
  /// <summary>Publishes domain events collected from aggregates to their handlers.</summary>
  public interface IDomainEventDispatcher {
    Task DispatchAsync(
        IReadOnlyCollection<IDomainEvent> domainEvents, CancellationToken cancellationToken);
  }
}

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kakehashi.SharedKernel;

namespace Kakehashi.Application.Abstractions.Messaging {
  public interface IDomainEventDispatcher {
    Task DispatchAsync(
        IReadOnlyCollection<IDomainEvent> domainEvents, CancellationToken cancellationToken);
  }
}

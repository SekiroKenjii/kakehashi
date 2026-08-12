using System.Threading;
using System.Threading.Tasks;

namespace Kakehashi.Application.Abstractions.Messaging {
  public interface ISender {
    Task<TResponse> Send<TResponse>(
        IRequest<TResponse> request, CancellationToken cancellationToken = default);
  }
}

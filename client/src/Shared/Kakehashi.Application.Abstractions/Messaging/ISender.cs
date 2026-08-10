using System.Threading;
using System.Threading.Tasks;

namespace Kakehashi.Application.Abstractions.Messaging {
  /// <summary>Sends a request to its handler and returns the response.</summary>
  public interface ISender {
    Task<TResponse> Send<TResponse>(
        IRequest<TResponse> request, CancellationToken cancellationToken = default);
  }
}

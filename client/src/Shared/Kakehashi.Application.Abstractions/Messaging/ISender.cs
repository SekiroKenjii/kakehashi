using System.Threading;
using System.Threading.Tasks;

namespace Kakehashi.Application.Abstractions.Messaging {
  /// <summary>Sends a request to its handler and returns the response.</summary>
  public interface ISender {
    /// <summary>Sends the request to its single handler, through the pipeline.</summary>
    /// <typeparam name="TResponse">The response type.</typeparam>
    Task<TResponse> Send<TResponse>(
        IRequest<TResponse> request, CancellationToken cancellationToken = default);
  }
}

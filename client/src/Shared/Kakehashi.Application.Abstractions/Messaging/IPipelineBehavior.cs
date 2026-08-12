using System.Threading;
using System.Threading.Tasks;

namespace Kakehashi.Application.Abstractions.Messaging {
  // Behaviors chain: each one must call next, or the handler never runs.
  public interface IPipelineBehavior<in TRequest, TResponse>
      where TRequest : IRequest<TResponse> {
    Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken);
  }
}

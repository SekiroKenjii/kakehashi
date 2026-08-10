using System.Threading;
using System.Threading.Tasks;

namespace Kakehashi.Application.Abstractions.Messaging {
  /// <summary>
  /// A cross-cutting step wrapped around request handling (for example logging, validation or
  /// transactions). Behaviors form a pipeline and must call <paramref name="next"/> to continue.
  /// </summary>
  /// <typeparam name="TRequest">The request type.</typeparam>
  /// <typeparam name="TResponse">The response type.</typeparam>
  public interface IPipelineBehavior<in TRequest, TResponse>
      where TRequest : IRequest<TResponse> {
    Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken);
  }
}

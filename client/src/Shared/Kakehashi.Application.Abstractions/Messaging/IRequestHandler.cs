using System.Threading;
using System.Threading.Tasks;

namespace Kakehashi.Application.Abstractions.Messaging {
  public interface IRequestHandler<in TRequest, TResponse>
      where TRequest : IRequest<TResponse> {
    Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken);
  }

  public interface IRequestHandler<in TRequest> : IRequestHandler<TRequest, Unit>
      where TRequest : IRequest {
  }
}

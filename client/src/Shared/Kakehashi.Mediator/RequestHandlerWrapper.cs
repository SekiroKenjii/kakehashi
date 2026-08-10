using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kakehashi.Application.Abstractions.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace Kakehashi.Mediator {
  internal abstract class RequestHandlerWrapperBase {
  }

  internal abstract class RequestHandlerWrapper<TResponse> : RequestHandlerWrapperBase {
    public abstract Task<TResponse> Handle(
        object request, IServiceProvider services, CancellationToken cancellationToken);
  }

  internal sealed class RequestHandlerWrapperImpl<TRequest, TResponse>
      : RequestHandlerWrapper<TResponse>
      where TRequest : IRequest<TResponse> {
    public override Task<TResponse> Handle(
        object request, IServiceProvider services, CancellationToken cancellationToken) {
      var typedRequest = (TRequest)request;

      Task<TResponse> HandlerCall() {
        var handler = services.GetRequiredService<IRequestHandler<TRequest, TResponse>>();
        return handler.Handle(typedRequest, cancellationToken);
      }

      RequestHandlerDelegate<TResponse> next = HandlerCall;
      var behaviors = services
          .GetServices<IPipelineBehavior<TRequest, TResponse>>()
          .Reverse()
          .ToArray();
      foreach (var behavior in behaviors) {
        var nextStep = next;
        next = () => behavior.Handle(typedRequest, nextStep, cancellationToken);
      }
      return next();
    }
  }
}

namespace Kakehashi.Application.Abstractions.Messaging {
  // Exactly one handler per request; more than one is a registration error.
  public interface IRequest<out TResponse> {
  }

  public interface IRequest : IRequest<Unit> {
  }
}

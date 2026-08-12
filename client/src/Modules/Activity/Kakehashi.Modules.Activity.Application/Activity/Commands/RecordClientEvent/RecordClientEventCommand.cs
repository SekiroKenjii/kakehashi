using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.SharedKernel;

namespace Kakehashi.Modules.Activity.Application.Activity.Commands.RecordClientEvent {
  // Nothing else in the request: whose feed comes from the token, and when it happened is the
  // server's clock — a client with a wrong clock could otherwise scatter rows through a history
  // that somebody reads in order.
  public sealed record RecordClientEventCommand(ClientActivityKind Kind) : IRequest<Result>;
}

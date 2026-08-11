using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.SharedKernel;

namespace Kakehashi.Modules.Activity.Application.Activity.Commands.RecordClientEvent {
  // Reports one fact this client knows about itself so it reaches the account's other devices.
  //
  // The module's first command, and the reason the module has a write path at all. Nothing else here
  // writes: see ClientActivityKind for why these two facts are the exception.
  //
  // Nothing else in the request. Whose feed comes from the token, and when it happened is the
  // server's clock — a client with a wrong clock could otherwise scatter rows through a history that
  // somebody reads in order.
  public sealed record RecordClientEventCommand(ClientActivityKind Kind) : IRequest<Result>;
}

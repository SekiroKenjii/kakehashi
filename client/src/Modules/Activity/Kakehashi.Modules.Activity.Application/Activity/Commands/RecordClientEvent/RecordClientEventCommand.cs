using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.SharedKernel;

namespace Kakehashi.Modules.Activity.Application.Activity.Commands.RecordClientEvent {
  /// <summary>
  /// Reports one fact this client knows about itself so it reaches the account's other devices.
  /// </summary>
  /// <remarks>
  /// The request carries only the kind: whose feed comes from the token, and the timestamp is the
  /// server's clock — a client with a wrong clock could otherwise scatter rows through a history
  /// that is read in order. See <see cref="ClientActivityKind"/> for what may be reported.
  /// </remarks>
  public sealed record RecordClientEventCommand(ClientActivityKind Kind) : IRequest<Result>;
}

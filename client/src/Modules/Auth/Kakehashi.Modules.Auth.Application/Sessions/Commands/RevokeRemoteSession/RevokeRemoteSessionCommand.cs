using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.SharedKernel;

namespace Kakehashi.Modules.Auth.Application.Sessions.Commands.RevokeRemoteSession {
  /// <summary>Revokes one of the user's sessions on the authorization server.</summary>
  public sealed record RevokeRemoteSessionCommand(string SessionId) : IRequest<Result>;
}

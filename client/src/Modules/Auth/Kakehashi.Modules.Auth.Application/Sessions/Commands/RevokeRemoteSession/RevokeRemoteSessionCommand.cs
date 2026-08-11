using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.SharedKernel;

namespace Kakehashi.Modules.Auth.Application.Sessions.Commands.RevokeRemoteSession {
  // Revokes one of the user's sessions on the authorization server.
  public sealed record RevokeRemoteSessionCommand(string SessionId) : IRequest<Result>;
}

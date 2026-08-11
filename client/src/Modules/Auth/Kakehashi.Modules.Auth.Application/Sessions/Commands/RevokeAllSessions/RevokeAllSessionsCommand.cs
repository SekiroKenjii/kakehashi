using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.SharedKernel;

namespace Kakehashi.Modules.Auth.Application.Sessions.Commands.RevokeAllSessions {
  // Revokes every session on the authorization server ("sign out everywhere"). The caller follows
  // up with SignOut.SignOutCommand to clear the local session.
  public sealed record RevokeAllSessionsCommand : IRequest<Result>;
}

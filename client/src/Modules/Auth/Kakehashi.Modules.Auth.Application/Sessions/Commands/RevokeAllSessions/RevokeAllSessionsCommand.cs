using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.SharedKernel;

namespace Kakehashi.Modules.Auth.Application.Sessions.Commands.RevokeAllSessions {
  /// <summary>
  /// Revokes every session on the authorization server ("sign out everywhere"). The caller follows
  /// up with <see cref="SignOut.SignOutCommand"/> to clear the local session.
  /// </summary>
  public sealed record RevokeAllSessionsCommand : IRequest<Result>;
}

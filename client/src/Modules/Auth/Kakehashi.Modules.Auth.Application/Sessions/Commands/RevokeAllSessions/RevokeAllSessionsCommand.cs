using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.SharedKernel;

namespace Kakehashi.Modules.Auth.Application.Sessions.Commands.RevokeAllSessions {
  // Does not clear the local session; the caller follows up with SignOutCommand.
  public sealed record RevokeAllSessionsCommand : IRequest<Result>;
}

using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.SharedKernel;

namespace Kakehashi.Modules.Auth.Application.Sessions.Commands.RevokeRemoteSession {
  public sealed record RevokeRemoteSessionCommand(string SessionId) : IRequest<Result>;
}

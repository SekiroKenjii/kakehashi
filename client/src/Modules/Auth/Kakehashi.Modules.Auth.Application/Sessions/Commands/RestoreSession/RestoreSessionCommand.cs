using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.SharedKernel;

namespace Kakehashi.Modules.Auth.Application.Sessions.Commands.RestoreSession {
  // Attempts to restore a session at startup by refreshing the persisted refresh token, without
  // user interaction. Fails when there is nothing stored or the refresh is rejected.
  public sealed record RestoreSessionCommand : IRequest<Result>;
}

using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.SharedKernel;

namespace Kakehashi.Modules.Auth.Application.Sessions.Commands.RestoreSession {
  public sealed record RestoreSessionCommand : IRequest<Result>;
}

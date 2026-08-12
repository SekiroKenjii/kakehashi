using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.SharedKernel;

namespace Kakehashi.Modules.Auth.Application.Account.Commands.UpdateRemoteProfile {
  public sealed record UpdateRemoteProfileCommand(string? DisplayName, string? Phone)
      : IRequest<Result>;
}

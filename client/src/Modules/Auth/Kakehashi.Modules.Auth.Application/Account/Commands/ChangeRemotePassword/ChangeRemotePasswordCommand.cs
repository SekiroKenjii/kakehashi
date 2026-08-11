using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.SharedKernel;

namespace Kakehashi.Modules.Auth.Application.Account.Commands.ChangeRemotePassword {
  // Changes the user's password on the authorization server.
  public sealed record ChangeRemotePasswordCommand(string CurrentPassword, string NewPassword)
      : IRequest<Result>;
}

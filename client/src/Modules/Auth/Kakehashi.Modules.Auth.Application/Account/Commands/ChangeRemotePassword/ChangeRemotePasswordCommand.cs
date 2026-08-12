using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.SharedKernel;

namespace Kakehashi.Modules.Auth.Application.Account.Commands.ChangeRemotePassword {
  public sealed record ChangeRemotePasswordCommand(string CurrentPassword, string NewPassword)
      : IRequest<Result>;
}

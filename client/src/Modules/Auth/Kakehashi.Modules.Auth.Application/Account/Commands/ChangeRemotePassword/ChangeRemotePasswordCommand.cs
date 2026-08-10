using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.SharedKernel;

namespace Kakehashi.Modules.Auth.Application.Account.Commands.ChangeRemotePassword {
  /// <summary>Changes the user's password on the authorization server.</summary>
  public sealed record ChangeRemotePasswordCommand(string CurrentPassword, string NewPassword)
      : IRequest<Result>;
}

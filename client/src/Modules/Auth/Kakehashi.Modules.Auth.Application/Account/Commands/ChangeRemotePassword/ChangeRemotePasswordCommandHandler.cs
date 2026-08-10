using System;
using System.Threading;
using System.Threading.Tasks;
using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.Modules.Auth.Application.Abstractions;
using Kakehashi.SharedKernel;

namespace Kakehashi.Modules.Auth.Application.Account.Commands.ChangeRemotePassword {
  /// <summary>Changes the password through the account gateway.</summary>
  public sealed class ChangeRemotePasswordCommandHandler
      : IRequestHandler<ChangeRemotePasswordCommand, Result> {
    private readonly IAccountGateway _account;

    public ChangeRemotePasswordCommandHandler(IAccountGateway account) {
      ArgumentNullException.ThrowIfNull(account);
      _account = account;
    }

    public Task<Result> Handle(
        ChangeRemotePasswordCommand request, CancellationToken cancellationToken) {
      return _account.ChangePasswordAsync(
          request.CurrentPassword, request.NewPassword, cancellationToken);
    }
  }
}

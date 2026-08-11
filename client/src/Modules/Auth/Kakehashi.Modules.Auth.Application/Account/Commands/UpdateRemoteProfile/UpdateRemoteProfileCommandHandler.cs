using System;
using System.Threading;
using System.Threading.Tasks;
using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.Modules.Auth.Application.Abstractions;
using Kakehashi.SharedKernel;

namespace Kakehashi.Modules.Auth.Application.Account.Commands.UpdateRemoteProfile {
  // Updates the profile through the account gateway.
  public sealed class UpdateRemoteProfileCommandHandler
      : IRequestHandler<UpdateRemoteProfileCommand, Result> {
    private readonly IAccountGateway _account;

    public UpdateRemoteProfileCommandHandler(IAccountGateway account) {
      ArgumentNullException.ThrowIfNull(account);
      _account = account;
    }

    public Task<Result> Handle(
        UpdateRemoteProfileCommand request, CancellationToken cancellationToken) {
      return _account.UpdateProfileAsync(request.DisplayName, request.Phone, cancellationToken);
    }
  }
}

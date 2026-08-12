using System;
using System.Threading;
using System.Threading.Tasks;
using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.Modules.Auth.Application.Abstractions;
using Kakehashi.SharedKernel;

namespace Kakehashi.Modules.Auth.Application.Sessions.Commands.RevokeRemoteSession {
  public sealed class RevokeRemoteSessionCommandHandler
      : IRequestHandler<RevokeRemoteSessionCommand, Result> {
    private readonly IAccountGateway _account;

    public RevokeRemoteSessionCommandHandler(IAccountGateway account) {
      ArgumentNullException.ThrowIfNull(account);
      _account = account;
    }

    public Task<Result> Handle(
        RevokeRemoteSessionCommand request, CancellationToken cancellationToken) {
      return _account.RevokeSessionAsync(request.SessionId, cancellationToken);
    }
  }
}

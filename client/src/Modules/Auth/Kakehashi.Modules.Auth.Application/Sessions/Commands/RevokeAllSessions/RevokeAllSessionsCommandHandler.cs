using System;
using System.Threading;
using System.Threading.Tasks;
using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.Modules.Auth.Application.Abstractions;
using Kakehashi.SharedKernel;

namespace Kakehashi.Modules.Auth.Application.Sessions.Commands.RevokeAllSessions {
  // Revokes all sessions through the account gateway.
  public sealed class RevokeAllSessionsCommandHandler
      : IRequestHandler<RevokeAllSessionsCommand, Result> {
    private readonly IAccountGateway _account;

    public RevokeAllSessionsCommandHandler(IAccountGateway account) {
      ArgumentNullException.ThrowIfNull(account);
      _account = account;
    }

    public Task<Result> Handle(
        RevokeAllSessionsCommand request, CancellationToken cancellationToken) {
      return _account.RevokeAllSessionsAsync(cancellationToken);
    }
  }
}

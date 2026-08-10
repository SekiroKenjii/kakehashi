using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.Modules.Auth.Application.Abstractions;
using Kakehashi.SharedKernel;

namespace Kakehashi.Modules.Auth.Application.Sessions.Queries.GetRemoteSessions {
  /// <summary>Fetches the session list through the account gateway.</summary>
  public sealed class GetRemoteSessionsQueryHandler
      : IRequestHandler<GetRemoteSessionsQuery, Result<IReadOnlyList<RemoteSessionDto>>> {
    private readonly IAccountGateway _account;

    public GetRemoteSessionsQueryHandler(IAccountGateway account) {
      ArgumentNullException.ThrowIfNull(account);
      _account = account;
    }

    public Task<Result<IReadOnlyList<RemoteSessionDto>>> Handle(
        GetRemoteSessionsQuery request, CancellationToken cancellationToken) {
      return _account.GetSessionsAsync(cancellationToken);
    }
  }
}

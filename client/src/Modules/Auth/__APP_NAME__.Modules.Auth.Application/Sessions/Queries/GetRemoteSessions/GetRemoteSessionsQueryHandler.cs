using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using __ROOT_NAMESPACE__.Application.Abstractions.Messaging;
using __ROOT_NAMESPACE__.Modules.Auth.Application.Abstractions;
using __ROOT_NAMESPACE__.SharedKernel;

namespace __ROOT_NAMESPACE__.Modules.Auth.Application.Sessions.Queries.GetRemoteSessions;

public sealed class GetRemoteSessionsQueryHandler
    : IRequestHandler<GetRemoteSessionsQuery, Result<IReadOnlyList<RemoteSessionDto>>>
{
    private readonly IAccountGateway _account;

    public GetRemoteSessionsQueryHandler(IAccountGateway account)
    {
        ArgumentNullException.ThrowIfNull(account);
        _account = account;
    }

    public Task<Result<IReadOnlyList<RemoteSessionDto>>> Handle(
        GetRemoteSessionsQuery request, CancellationToken cancellationToken)
    {
        return _account.GetSessionsAsync(cancellationToken);
    }
}

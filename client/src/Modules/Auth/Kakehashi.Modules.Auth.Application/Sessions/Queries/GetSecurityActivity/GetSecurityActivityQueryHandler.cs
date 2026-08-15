using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.Modules.Auth.Application.Abstractions;
using Kakehashi.SharedKernel;

namespace Kakehashi.Modules.Auth.Application.Sessions.Queries.GetSecurityActivity;

public sealed class GetSecurityActivityQueryHandler
    : IRequestHandler<GetSecurityActivityQuery, Result<IReadOnlyList<SecurityEventDto>>>
{
    private readonly IAccountGateway _account;

    public GetSecurityActivityQueryHandler(IAccountGateway account)
    {
        ArgumentNullException.ThrowIfNull(account);
        _account = account;
    }

    public Task<Result<IReadOnlyList<SecurityEventDto>>> Handle(
        GetSecurityActivityQuery request, CancellationToken cancellationToken)
    {
        return _account.GetSecurityActivityAsync(request.Take, cancellationToken);
    }
}

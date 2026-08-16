using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using __ROOT_NAMESPACE__.Application.Abstractions.Messaging;
using __ROOT_NAMESPACE__.Modules.Auth.Application.Abstractions;
using __ROOT_NAMESPACE__.SharedKernel;

namespace __ROOT_NAMESPACE__.Modules.Auth.Application.Sessions.Queries.GetSecurityActivity;

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

using System;
using System.Threading;
using System.Threading.Tasks;
using __ROOT_NAMESPACE__.Application.Abstractions.Messaging;
using __ROOT_NAMESPACE__.Modules.Auth.Application.Abstractions;
using __ROOT_NAMESPACE__.SharedKernel;

namespace __ROOT_NAMESPACE__.Modules.Auth.Application.Sessions.Commands.RevokeAllSessions;

public sealed class RevokeAllSessionsCommandHandler
    : IRequestHandler<RevokeAllSessionsCommand, Result>
{
    private readonly IAccountGateway _account;

    public RevokeAllSessionsCommandHandler(IAccountGateway account)
    {
        ArgumentNullException.ThrowIfNull(account);
        _account = account;
    }

    public Task<Result> Handle(
        RevokeAllSessionsCommand request, CancellationToken cancellationToken)
    {
        return _account.RevokeAllSessionsAsync(cancellationToken);
    }
}

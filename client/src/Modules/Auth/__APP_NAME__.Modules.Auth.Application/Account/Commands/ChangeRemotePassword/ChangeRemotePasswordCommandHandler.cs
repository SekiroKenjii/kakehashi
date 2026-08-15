using System;
using System.Threading;
using System.Threading.Tasks;
using __ROOT_NAMESPACE__.Application.Abstractions.Messaging;
using __ROOT_NAMESPACE__.Modules.Auth.Application.Abstractions;
using __ROOT_NAMESPACE__.SharedKernel;

namespace __ROOT_NAMESPACE__.Modules.Auth.Application.Account.Commands.ChangeRemotePassword;

public sealed class ChangeRemotePasswordCommandHandler
    : IRequestHandler<ChangeRemotePasswordCommand, Result>
{
    private readonly IAccountGateway _account;

    public ChangeRemotePasswordCommandHandler(IAccountGateway account)
    {
        ArgumentNullException.ThrowIfNull(account);
        _account = account;
    }

    public Task<Result> Handle(
        ChangeRemotePasswordCommand request, CancellationToken cancellationToken)
    {
        return _account.ChangePasswordAsync(
            request.CurrentPassword, request.NewPassword, cancellationToken);
    }
}

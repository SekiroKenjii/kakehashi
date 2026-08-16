using System;
using System.Threading;
using System.Threading.Tasks;
using __ROOT_NAMESPACE__.Application.Abstractions.Messaging;
using __ROOT_NAMESPACE__.Modules.Auth.Application.Abstractions;
using __ROOT_NAMESPACE__.SharedKernel;

namespace __ROOT_NAMESPACE__.Modules.Auth.Application.Account.Commands.UpdateRemoteProfile;

public sealed class UpdateRemoteProfileCommandHandler
    : IRequestHandler<UpdateRemoteProfileCommand, Result>
{
    private readonly IAccountGateway _account;

    public UpdateRemoteProfileCommandHandler(IAccountGateway account)
    {
        ArgumentNullException.ThrowIfNull(account);
        _account = account;
    }

    public Task<Result> Handle(
        UpdateRemoteProfileCommand request, CancellationToken cancellationToken)
    {
        return _account.UpdateProfileAsync(request.DisplayName, request.Phone, cancellationToken);
    }
}

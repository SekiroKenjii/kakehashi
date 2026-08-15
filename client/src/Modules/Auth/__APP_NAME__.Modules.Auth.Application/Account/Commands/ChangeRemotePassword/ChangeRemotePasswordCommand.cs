using __ROOT_NAMESPACE__.Application.Abstractions.Messaging;
using __ROOT_NAMESPACE__.SharedKernel;

namespace __ROOT_NAMESPACE__.Modules.Auth.Application.Account.Commands.ChangeRemotePassword;

/// <summary>Changes the user's password on the authorization server.</summary>
public sealed record ChangeRemotePasswordCommand(string CurrentPassword, string NewPassword)
    : IRequest<Result>;

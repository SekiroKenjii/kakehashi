using __ROOT_NAMESPACE__.Application.Abstractions.Messaging;
using __ROOT_NAMESPACE__.SharedKernel;

namespace __ROOT_NAMESPACE__.Modules.Auth.Application.Account.Commands.UpdateRemoteProfile;

/// <summary>Updates the user's display name and phone number on the authorization server.</summary>
public sealed record UpdateRemoteProfileCommand(string? DisplayName, string? Phone)
    : IRequest<Result>;

using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.SharedKernel;

namespace Kakehashi.Modules.Auth.Application.Account.Commands.UpdateRemoteProfile;

/// <summary>Updates the user's display name and phone number on the authorization server.</summary>
public sealed record UpdateRemoteProfileCommand(string? DisplayName, string? Phone)
    : IRequest<Result>;

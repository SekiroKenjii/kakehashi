using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.SharedKernel;

namespace Kakehashi.Modules.Auth.Application.Account.Queries.GetRemoteProfile;

/// <summary>Reads the user's profile from the authorization server.</summary>
public sealed record GetRemoteProfileQuery : IRequest<Result<RemoteProfileDto>>;

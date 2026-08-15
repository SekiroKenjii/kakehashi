using __ROOT_NAMESPACE__.Application.Abstractions.Messaging;
using __ROOT_NAMESPACE__.SharedKernel;

namespace __ROOT_NAMESPACE__.Modules.Auth.Application.Account.Queries.GetRemoteProfile;

/// <summary>Reads the user's profile from the authorization server.</summary>
public sealed record GetRemoteProfileQuery : IRequest<Result<RemoteProfileDto>>;

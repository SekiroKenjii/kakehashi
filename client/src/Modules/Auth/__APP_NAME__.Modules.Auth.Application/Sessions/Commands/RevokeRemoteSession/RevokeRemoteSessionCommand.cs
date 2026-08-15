using __ROOT_NAMESPACE__.Application.Abstractions.Messaging;
using __ROOT_NAMESPACE__.SharedKernel;

namespace __ROOT_NAMESPACE__.Modules.Auth.Application.Sessions.Commands.RevokeRemoteSession;

/// <summary>Revokes one of the user's sessions on the authorization server.</summary>
public sealed record RevokeRemoteSessionCommand(string SessionId) : IRequest<Result>;

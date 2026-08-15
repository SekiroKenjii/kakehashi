using __ROOT_NAMESPACE__.Application.Abstractions.Messaging;
using __ROOT_NAMESPACE__.SharedKernel;

namespace __ROOT_NAMESPACE__.Modules.Auth.Application.Sessions.Commands.RevokeAllSessions;

/// <summary>
/// Revokes every session on the authorization server ("sign out everywhere"). The caller follows
/// up with <see cref="SignOut.SignOutCommand"/> to clear the local session.
/// </summary>
public sealed record RevokeAllSessionsCommand : IRequest<Result>;

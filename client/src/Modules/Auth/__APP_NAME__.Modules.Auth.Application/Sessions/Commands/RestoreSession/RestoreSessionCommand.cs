using __ROOT_NAMESPACE__.Application.Abstractions.Messaging;
using __ROOT_NAMESPACE__.SharedKernel;

namespace __ROOT_NAMESPACE__.Modules.Auth.Application.Sessions.Commands.RestoreSession;

/// <summary>
/// Attempts to restore a session at startup by refreshing the persisted refresh token, without
/// user interaction. Fails when there is nothing stored or the refresh is rejected.
/// </summary>
public sealed record RestoreSessionCommand : IRequest<Result>;

using __ROOT_NAMESPACE__.Application.Abstractions.Messaging;

namespace __ROOT_NAMESPACE__.Modules.Auth.Application.Sessions.Queries.GetCurrentSession;

/// <summary>Returns the current authentication state for presentation.</summary>
public sealed record GetCurrentSessionQuery : IRequest<SessionDto>;

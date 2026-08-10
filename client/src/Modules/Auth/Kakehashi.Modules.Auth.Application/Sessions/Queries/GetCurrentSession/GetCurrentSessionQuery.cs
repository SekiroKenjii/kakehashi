using Kakehashi.Application.Abstractions.Messaging;

namespace Kakehashi.Modules.Auth.Application.Sessions.Queries.GetCurrentSession {
  /// <summary>Returns the current authentication state for presentation.</summary>
  public sealed record GetCurrentSessionQuery : IRequest<SessionDto>;
}

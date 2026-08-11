using Kakehashi.Application.Abstractions.Messaging;

namespace Kakehashi.Modules.Auth.Application.Sessions.Queries.GetCurrentSession {
  // Returns the current authentication state for presentation.
  public sealed record GetCurrentSessionQuery : IRequest<SessionDto>;
}

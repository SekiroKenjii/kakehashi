using Kakehashi.Application.Abstractions.Messaging;

namespace Kakehashi.Modules.Auth.Application.Sessions.Queries.GetCurrentSession {
  public sealed record GetCurrentSessionQuery : IRequest<SessionDto>;
}

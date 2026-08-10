using System.Threading;
using System.Threading.Tasks;
using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.Modules.Auth.Application.Abstractions;

namespace Kakehashi.Modules.Auth.Application.Sessions.Queries.GetCurrentSession {
  /// <summary>Projects the current <see cref="Domain.AuthSession"/> to a <see cref="SessionDto"/>.</summary>
  public sealed class GetCurrentSessionQueryHandler
      : IRequestHandler<GetCurrentSessionQuery, SessionDto> {
    private readonly IAuthSessionAccessor _session;

    public GetCurrentSessionQueryHandler(IAuthSessionAccessor session) {
      _session = session;
    }

    public Task<SessionDto> Handle(GetCurrentSessionQuery request, CancellationToken cancellationToken) {
      var current = _session.Current;
      var dto = current is null
          ? new SessionDto(
              IsAuthenticated: false,
              DisplayName: null,
              Email: null,
              ExpiresAtUtc: null,
              SignedInAtUtc: null,
              Roles: [])
          : new SessionDto(
              IsAuthenticated: true,
              current.DisplayName,
              current.Email,
              current.ExpiresAtUtc,
              _session.SignedInAtUtc,
              current.Roles);
      return Task.FromResult(dto);
    }
  }
}

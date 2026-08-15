using System.Threading;
using System.Threading.Tasks;
using __ROOT_NAMESPACE__.Application.Abstractions.Messaging;
using __ROOT_NAMESPACE__.Modules.Auth.Application.Abstractions;

namespace __ROOT_NAMESPACE__.Modules.Auth.Application.Sessions.Queries.GetCurrentSession;

public sealed class GetCurrentSessionQueryHandler
    : IRequestHandler<GetCurrentSessionQuery, SessionDto>
{
    private readonly IAuthSessionAccessor _session;

    public GetCurrentSessionQueryHandler(IAuthSessionAccessor session)
    {
        _session = session;
    }

    public Task<SessionDto> Handle(GetCurrentSessionQuery request, CancellationToken cancellationToken)
    {
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

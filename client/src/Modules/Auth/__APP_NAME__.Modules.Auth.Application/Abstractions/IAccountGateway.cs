using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using __ROOT_NAMESPACE__.Modules.Auth.Application.Account;
using __ROOT_NAMESPACE__.Modules.Auth.Application.Sessions;
using __ROOT_NAMESPACE__.SharedKernel;

namespace __ROOT_NAMESPACE__.Modules.Auth.Application.Abstractions;

/// <summary>
/// Port over the authorization server's account endpoints (profile, sessions and security
/// activity), called with the current user's access token. The adapter lives in the UI layer.
/// </summary>
public interface IAccountGateway
{
    Task<Result<IReadOnlyList<RemoteSessionDto>>> GetSessionsAsync(
        CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<SecurityEventDto>>> GetSecurityActivityAsync(
        int take, CancellationToken cancellationToken);

    Task<Result> RevokeSessionAsync(string sessionId, CancellationToken cancellationToken);

    Task<Result> RevokeAllSessionsAsync(CancellationToken cancellationToken);

    Task<Result<RemoteProfileDto>> GetProfileAsync(CancellationToken cancellationToken);

    Task<Result> UpdateProfileAsync(
        string? displayName, string? phone, CancellationToken cancellationToken);

    Task<Result> ChangePasswordAsync(
        string currentPassword, string newPassword, CancellationToken cancellationToken);
}

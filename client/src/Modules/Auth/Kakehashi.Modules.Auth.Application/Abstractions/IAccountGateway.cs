using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kakehashi.Modules.Auth.Application.Account;
using Kakehashi.Modules.Auth.Application.Sessions;
using Kakehashi.SharedKernel;

namespace Kakehashi.Modules.Auth.Application.Abstractions {
  // No method takes a token: the adapter attaches the current user's access token itself.
  public interface IAccountGateway {
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
}

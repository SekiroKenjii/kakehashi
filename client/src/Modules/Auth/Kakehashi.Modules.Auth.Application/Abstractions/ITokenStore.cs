using System.Threading;
using System.Threading.Tasks;

namespace Kakehashi.Modules.Auth.Application.Abstractions {
  // The adapter must encrypt the refresh token at rest; it outlives the process.
  public interface ITokenStore {
    Task<string?> LoadRefreshTokenAsync(CancellationToken cancellationToken);

    Task SaveRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken);

    Task ClearAsync(CancellationToken cancellationToken);
  }
}

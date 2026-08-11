using System.Threading;
using System.Threading.Tasks;

namespace Kakehashi.Modules.Auth.Application.Abstractions {
  // Securely persists the refresh token between runs so the user stays signed in across restarts.
  // The concrete adapter (in the UI layer) encrypts the value at rest.
  public interface ITokenStore {
    Task<string?> LoadRefreshTokenAsync(CancellationToken cancellationToken);

    Task SaveRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken);

    Task ClearAsync(CancellationToken cancellationToken);
  }
}

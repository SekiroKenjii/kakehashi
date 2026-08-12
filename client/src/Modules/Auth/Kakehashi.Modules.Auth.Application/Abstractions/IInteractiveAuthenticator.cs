using System.Threading;
using System.Threading.Tasks;
using Kakehashi.Modules.Auth.Domain;
using Kakehashi.SharedKernel;

namespace Kakehashi.Modules.Auth.Application.Abstractions {
  // Two adapters implement this — system browser (Authorization Code + PKCE) and in-app
  // credentials. Which one is registered is a composition decision this layer must not know.
  public interface IInteractiveAuthenticator {
    // credentials is ignored by the browser adapter, which collects them on the server's own page.
    Task<Result<AuthSession>> LoginAsync(
        SignInCredentials? credentials, CancellationToken cancellationToken);

    Task<Result<AuthSession>> RefreshAsync(string refreshToken, CancellationToken cancellationToken);

    // Best-effort. Takes the whole session rather than one token because the adapters need
    // different parts: the browser flow sends the id token as id_token_hint to the end-session
    // endpoint, the in-app flow authenticates its sign-out call with the access token.
    Task LogoutAsync(AuthSession? session, CancellationToken cancellationToken);
  }
}

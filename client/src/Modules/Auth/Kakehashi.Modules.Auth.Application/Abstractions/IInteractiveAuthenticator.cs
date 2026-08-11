using System.Threading;
using System.Threading.Tasks;
using Kakehashi.Modules.Auth.Domain;
using Kakehashi.SharedKernel;

namespace Kakehashi.Modules.Auth.Application.Abstractions {
  // Port for obtaining and ending a session at the authorization server. Concrete adapters live in
  // the UI layer: one signs in through the system browser (Authorization Code + PKCE), one posts
  // credentials from the app itself. Which one is registered is a composition decision; this layer
  // does not know and must not care.
  public interface IInteractiveAuthenticator {
    // Signs in and returns the resulting session.
    //
    // credentials: required by adapters that authenticate in-app, ignored by adapters that hand
    // the user to the authorization server's own page.
    Task<Result<AuthSession>> LoginAsync(
        SignInCredentials? credentials, CancellationToken cancellationToken);

    // Exchanges a refresh token for a fresh session, without user interaction.
    Task<Result<AuthSession>> RefreshAsync(string refreshToken, CancellationToken cancellationToken);

    // Best-effort end-session at the authorization server.
    //
    // session: the session being ended, or null when there is none. The whole session rather than
    // one token because the two adapters need different parts of it: the browser flow sends the id
    // token as id_token_hint to the end-session endpoint, the in-app flow authenticates its
    // sign-out call with the access token.
    Task LogoutAsync(AuthSession? session, CancellationToken cancellationToken);
  }
}

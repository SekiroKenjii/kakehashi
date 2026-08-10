using System.Threading;
using System.Threading.Tasks;
using Kakehashi.Modules.Auth.Domain;
using Kakehashi.SharedKernel;

namespace Kakehashi.Modules.Auth.Application.Abstractions {
  /// <summary>
  /// Port for obtaining and ending a session at the authorization server. Concrete adapters live in
  /// the UI layer: one signs in through the system browser (Authorization Code + PKCE), one posts
  /// credentials from the app itself. Which one is registered is a composition decision; this layer
  /// does not know and must not care.
  /// </summary>
  public interface IInteractiveAuthenticator {
    /// <summary>Signs in and returns the resulting session.</summary>
    /// <param name="credentials">
    /// Required by adapters that authenticate in-app, ignored by adapters that hand the user to the
    /// authorization server's own page.
    /// </param>
    Task<Result<AuthSession>> LoginAsync(
        SignInCredentials? credentials, CancellationToken cancellationToken);

    /// <summary>Exchanges a refresh token for a fresh session, without user interaction.</summary>
    Task<Result<AuthSession>> RefreshAsync(string refreshToken, CancellationToken cancellationToken);

    /// <summary>Best-effort end-session at the authorization server.</summary>
    /// <param name="session">
    /// The session being ended, or null when there is none. The whole session rather than one token
    /// because the two adapters need different parts of it: the browser flow sends the id token as
    /// <c>id_token_hint</c> to the end-session endpoint, the in-app flow authenticates its sign-out
    /// call with the access token.
    /// </param>
    Task LogoutAsync(AuthSession? session, CancellationToken cancellationToken);
  }
}

using System.Threading;
using System.Threading.Tasks;

namespace Kakehashi.Application.Abstractions.Security {
  /// <summary>
  /// Supplies the bearer access token for outbound calls to the backend. The host's backend client
  /// asks for a token per request and, when one is returned, adds it as an
  /// <c>Authorization: Bearer</c> header; a <see langword="null"/> result means "no token" and the
  /// request is sent unauthenticated.
  /// </summary>
  /// <remarks>
  /// The host registers a no-op implementation by default; the Auth module replaces it with a
  /// session-backed provider that refreshes tokens near expiry. The contract lives here so host
  /// infrastructure and feature modules can both depend on it without referencing each other.
  /// </remarks>
  public interface IAccessTokenProvider {
    /// <summary>Returns the current access token, or <see langword="null"/> when none is available.</summary>
    ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default);
  }
}

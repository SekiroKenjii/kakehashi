namespace Kakehashi.Modules.Auth.Application.Abstractions {
  /// <summary>
  /// The credentials an in-app sign-in posts to the authorization server.
  /// </summary>
  /// <remarks>
  /// Optional wherever it appears, because a browser-based flow collects credentials on the
  /// authorization server's own page and this application never sees them. That is the whole point
  /// of the browser flow, and it is why the credential is a parameter rather than a requirement.
  /// </remarks>
  public sealed record SignInCredentials(string Email, string Password);
}

namespace Kakehashi.Modules.Auth.Application.Abstractions {
  // Optional wherever it appears: a browser-based flow collects credentials on the authorization
  // server's own page and this application never sees them.
  public sealed record SignInCredentials(string Email, string Password);
}

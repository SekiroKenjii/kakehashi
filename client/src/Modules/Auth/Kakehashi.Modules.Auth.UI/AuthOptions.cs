namespace Kakehashi.Modules.Auth.UI {
  // With Authority empty the module stays inert: the login gate is skipped and the app runs
  // unauthenticated.
  public sealed class AuthOptions {
    public const string SectionName = "Auth";

    // The discovery document is fetched from here, so this must be the issuer root.
    public string Authority { get; set; } = string.Empty;

    public string ClientId { get; set; } = string.Empty;

    public AuthMode Mode { get; set; } = AuthMode.InApp;

    // Leave empty for a public client (PKCE-only, recommended).
    public string? ClientSecret { get; set; }

    // offline_access is required to obtain a refresh token.
    public string Scope { get; set; } = "openid profile email roles offline_access";

    // RFC 8252 loopback: must be registered with the authorization server and point at 127.0.0.1
    // on a free port.
    public string RedirectUri { get; set; } = "http://127.0.0.1:8765/";

    public int RefreshSkewSeconds { get; set; } = 60;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Authority) && !string.IsNullOrWhiteSpace(ClientId);
  }
}

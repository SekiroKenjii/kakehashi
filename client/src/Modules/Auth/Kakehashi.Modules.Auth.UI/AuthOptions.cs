namespace Kakehashi.Modules.Auth.UI {
  // Strongly-typed configuration for the Auth module, bound from the Auth section of
  // appsettings.json. When Authority is empty the module stays inert: the login
  // gate is skipped and the app runs unauthenticated.
  public sealed class AuthOptions {
    // The configuration section these options bind to.
    public const string SectionName = "Auth";

    // The OpenID Connect issuer/authority (its discovery document is fetched from here).
    public string Authority { get; set; } = string.Empty;

    // The OAuth client id registered for this desktop app (a public client).
    public string ClientId { get; set; } = string.Empty;

    // Where the user types their password. See AuthMode.
    public AuthMode Mode { get; set; } = AuthMode.InApp;

    // Optional client secret. Leave empty for a public client (PKCE-only, recommended).
    public string? ClientSecret { get; set; }

    // Requested scopes. offline_access is required to obtain a refresh token.
    public string Scope { get; set; } = "openid profile email roles offline_access";

    // The loopback redirect URI (RFC 8252). Must be registered with the authorization server and
    // point at 127.0.0.1 on a free port, e.g. http://127.0.0.1:8765/.
    public string RedirectUri { get; set; } = "http://127.0.0.1:8765/";

    // How many seconds before expiry an access token is proactively refreshed.
    public int RefreshSkewSeconds { get; set; } = 60;

    // Whether enough configuration is present to attempt authentication.
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Authority) && !string.IsNullOrWhiteSpace(ClientId);
  }
}

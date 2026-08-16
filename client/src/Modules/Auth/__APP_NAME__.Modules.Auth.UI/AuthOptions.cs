namespace __ROOT_NAMESPACE__.Modules.Auth.UI;

/// <summary>
/// Strongly-typed configuration for the Auth module, bound from the <c>Auth</c> section of
/// <c>appsettings.json</c>. When <see cref="Authority"/> is empty the module stays inert: the login
/// gate is skipped and the app runs unauthenticated.
/// </summary>
public sealed class AuthOptions
{
    /// <summary>The configuration section these options bind to.</summary>
    public const string SectionName = "Auth";

    /// <summary>The OpenID Connect issuer/authority (its discovery document is fetched from here).</summary>
    public string Authority { get; set; } = string.Empty;

    /// <summary>The OAuth client id registered for this desktop app (a public client).</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Where the user types their password. See <see cref="AuthMode"/>.</summary>
    public AuthMode Mode { get; set; } = AuthMode.InApp;

    /// <summary>Optional client secret. Leave empty for a public client (PKCE-only, recommended).</summary>
    public string? ClientSecret { get; set; }

    /// <summary>Requested scopes. <c>offline_access</c> is required to obtain a refresh token.</summary>
    public string Scope { get; set; } = "openid profile email roles offline_access";

    /// <summary>
    /// The loopback redirect URI (RFC 8252). Must be registered with the authorization server and
    /// point at <c>127.0.0.1</c> on a free port, e.g. <c>http://127.0.0.1:8765/</c>.
    /// </summary>
    public string RedirectUri { get; set; } = "http://127.0.0.1:8765/";

    /// <summary>How many seconds before expiry an access token is proactively refreshed.</summary>
    public int RefreshSkewSeconds { get; set; } = 60;

    /// <summary>Whether enough configuration is present to attempt authentication.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Authority) && !string.IsNullOrWhiteSpace(ClientId);
}

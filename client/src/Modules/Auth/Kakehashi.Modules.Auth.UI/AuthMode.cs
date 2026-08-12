namespace Kakehashi.Modules.Auth.UI {
  // Where the user types their password.
  public enum AuthMode {
    // Into this application, which posts them to the authorization server. The default, because
    // the default authority is this product's own backend: a browser round trip would cross no new
    // trust boundary and costs a focus-stealing window plus a loopback listener corporate
    // firewalls dislike.
    InApp,

    // Into the authorization server's own page, via Authorization Code + PKCE. Required once
    // AuthOptions.Authority points at someone else's identity provider — Entra, Okta, Google —
    // where the point is that this application never sees the password, SSO/MFA/conditional access
    // live on that page, and password grants are refused.
    Browser,
  }
}

namespace Kakehashi.Modules.Auth.UI {
  // Where the user types their password.
  public enum AuthMode {
    // Into this application, which posts them to the authorization server. The default, because
    // the default authorization server is this product's own backend: bouncing through a browser
    // to reach it moves the password across no new trust boundary and costs the user a window
    // that steals focus plus a loopback listener corporate firewalls dislike.
    InApp,

    // Into the authorization server's own page, in the system browser, via Authorization Code +
    // PKCE. The correct answer the moment AuthOptions.Authority points at someone
    // else's identity provider — Entra, Okta, Google — because then the point is precisely that
    // this application never sees the password, and because SSO, MFA and conditional access all
    // live on that page. Those providers refuse password grants for exactly this reason.
    Browser,
  }
}

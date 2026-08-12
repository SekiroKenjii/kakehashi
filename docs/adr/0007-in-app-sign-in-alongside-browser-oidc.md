# 0007. In-app sign-in issues tokens through the same OIDC provider

Date: 2026-08-12
Status: accepted

## Context

The client authenticated through Authorization Code + PKCE in the system browser. That flow is
right when the identity provider is a third party (Entra, Okta, Google): the password is typed
into the provider's page and never reaches the app, and SSO, MFA and conditional access live
there. None of that applied here — the provider is this very server process — so the password
crossed the same trust boundary either way, while the user paid with a focus-stealing browser
window and a loopback listener that corporate firewalls dislike.

## Decision

The default sign-in posts email/password to an in-app endpoint
(server/internal/modules/account/rpc/signin_inapp.go). After authenticating and starting a
session, the handler builds a synthetic, already-authenticated authorization request and passes it
to the embedded provider's own op.CreateTokenResponse, so token minting, signing and claim
assembly stay the provider's. The response is shaped as a standard OAuth token response — one
client type for both modes, refresh through the standard token endpoint — and the browser flow
stays mounted.

## Consequences

The two sign-in modes cannot drift into issuing different tokens. Three details are load-bearing:
the synthetic request must carry ResponseType "code" and the offline_access scope, or op withholds
the refresh token and the client returns to the sign-in form every ten minutes; the issuer must be
set via op.ContextWithIssuer, or the JWT ships without an "iss" claim and every verifier —
including this server's — rejects it; the code and rotated-refresh-token arguments stay empty,
since a fabricated code would put a c_hash in the ID token hashing something the client never saw.
The invariant to respect: the moment Auth:Authority points at an external IdP, the client must
switch back to browser mode — real IdPs refuse password grants because they defeat MFA.

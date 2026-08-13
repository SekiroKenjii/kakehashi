# 0006. An ID token cannot authenticate an API call

Date: 2026-08-12
Status: accepted

## Context

Sign-in hands the client an ID token beside the access token. Both are JWTs from the same issuer,
signed with the same key, carrying the same `aud` and `client_id`. The RPC verifier checks tokens
locally — signature, issuer, expiry — and `op.VerifyAccessToken` checks nothing about what kind
of token it holds. An ID token from this issuer therefore passed every check and authenticated every
endpoint for its full one-hour lifetime; because nothing about it names a session, it kept working
after sign-out, after session revocation, and after account deactivation.

## Decision

`verifier.Verify` rejects any token without the `sid` claim. `sid` is set by
`GetPrivateClaimsFromRequest`, which runs only when an access token is minted — `sessionIDOf`
covers both grants that produce one, the authorization code and the refresh — so an access token
from this provider always has it and an ID token never does. As the positive half of the same
check, a token carrying any ID-token marker (`at_hash`, `azp`, `auth_time`) is also rejected,
guarding against a future grant that mints an access token by a path skipping the private claims.

## Consequences

A token that outlives revocation is worse than a long-lived one; `sid` is also what makes the
access token revocable at all, because the session it names is what sign-out deletes. `aud` and
`client_id` were rejected as discriminators: both token kinds carry the same values. The invariant
a future grant type must respect: any path that mints an access token must set `sid` (extend
`sessionIDOf`), or the verifier will reject its tokens. Roles are deliberately not read from the
token — authorization is resolved per request, not from a token that lives ten minutes.

// Package domain holds the account module's entities and the rules they enforce. It imports the
// platform's error types and its password hashing, and nothing else — no SQL, no OpenID Connect,
// no other module — so the rules in here are testable without a database or a provider.
//
// Four aggregate roots — Account, UserSession, AuthRequest, SigningKey — each in its own file with
// its own lifecycle and consistency boundary. SecurityEvent is an append-only record, not an
// aggregate, and deliberately outlives the account it describes. IssuedToken is an entity inside
// UserSession, not a root: docs/adr/0014-issuedtoken-is-not-an-aggregate-root.md
package domain

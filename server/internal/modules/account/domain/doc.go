// Package domain holds the account module's entities and the rules they enforce.
//
// It imports the platform's error types and its password hashing, and nothing else. No SQL, no
// OpenID Connect, no other module — which is what lets the rules in here be tested without
// standing up a database or a provider.
//
// Account, UserSession, AuthRequest and SigningKey are the aggregate roots. SecurityEvent is not
// one: it is append-only, written once and never changed, and deliberately outlives the account it
// describes.
//
// IssuedToken is an entity inside UserSession rather than a root of its own: a token means nothing
// without the session it was issued under, and ending the session must end it. That is why the
// store's foreign key cascades — the database enforces the invariant even when a delete arrives
// from somewhere the service never sees.
//
// The test, when adding the next type here: can it be deleted on its own without leaving something
// else in a state its own rules forbid? If not, it belongs inside an existing root.
package domain

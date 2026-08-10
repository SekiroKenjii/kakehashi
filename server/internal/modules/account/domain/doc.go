// Package domain holds the account module's entities and the rules they enforce.
//
// It imports the platform's error types and its password hashing, and nothing else. No SQL, no
// OpenID Connect, no other module — which is what lets the rules in here be tested without
// standing up a database or a provider.
//
// # The aggregates
//
// Four roots, each in its own file, each with its own lifecycle and its own consistency boundary:
//
//	Account       account.go        who someone is: credentials, profile, roles.
//	UserSession   session.go        one sign-in — and the IssuedToken entities inside it.
//	AuthRequest   authrequest.go    one in-flight browser authorization, /authorize to /token.
//	SigningKey    signingkey.go     the provider's token-signing key.
//
// SecurityEvent (securityevent.go) is not an aggregate. It is an append-only record: written
// once, never changed, and deliberately outliving the account it describes.
//
// # Why IssuedToken is not a root
//
// A token has no life of its own. It is issued under a session, it means nothing without one, and
// ending the session must end it — which is a consistency rule, not a cleanup preference. That
// makes the session the boundary and the token an entity inside it, and it is why the store's
// foreign key cascades: the database enforces the invariant even when a delete arrives from
// somewhere the service never sees.
//
// The practical test, when adding the next type here: can it be deleted on its own without
// leaving something else in a state its own rules forbid? If not, it belongs inside an existing
// root rather than beside it.
package domain

// Package auth declares who the caller is, and the contracts for finding out what they may do.
// The contracts live in the platform because the kernel installs the middleware and the kernel
// may not import a module; the account module implements them and publishes them on the registry.
// Only the account module may import an OpenID Connect or JWT library (tools/archlint); everything
// else, the mux included, deals in Subject and never sees a token.
package auth

import "context"

// Subject is an authenticated caller, reduced to what the rest of the server needs.
//
// Deliberately not the token and not the full claim set: a handler holding only a Subject cannot
// re-verify a token or forward it anywhere.
type Subject struct {
	// ID is the stable identifier for the user, from the token's subject claim.
	ID string

	// Email and Name are for display and audit. Either may be empty.
	Email string
	Name  string

	// SessionID identifies which sign-in this request belongs to, when the verifier knows.
	// The account page uses it to mark "this device" in the session list; nothing else should
	// treat it as more than a correlation id.
	SessionID string
}

// Verifier turns a bearer token into a Subject, or an error.
//
// Implemented by the account module and published on the kernel. It is optional: with no verifier
// registered every request is anonymous and the server still starts.
type Verifier interface {
	// Verify checks the token's signature, issuer, audience and expiry, and returns who it
	// belongs to. It must not trust any claim it has not verified.
	Verify(ctx context.Context, token string) (Subject, error)
}

// subjectKey is unexported so nothing outside this package can write a Subject into a context. The
// only way one gets in is through the middleware that verified it.
type subjectKey struct{}

// WithSubject returns a context carrying the authenticated caller.
func WithSubject(ctx context.Context, subject Subject) context.Context {
	return context.WithValue(ctx, subjectKey{}, subject)
}

// SubjectFrom returns the authenticated caller, reporting false when the request was anonymous.
//
// Handlers that require a caller treat false as errs.Unauthenticated rather than falling back to
// a default.
func SubjectFrom(ctx context.Context) (Subject, bool) {
	subject, ok := ctx.Value(subjectKey{}).(Subject)
	return subject, ok
}

// Package auth declares who the caller is, and the contract for finding out.
//
// The interface lives here rather than in the identity module for one reason: the kernel installs
// the authentication middleware, and the kernel may not import a module. Declaring the contract in
// the platform lets identity implement it, publish it on the registry, and have the mux pick it up
// without anyone importing anything they are not allowed to.
//
// It also keeps the fence in tools/archlint honest. Only the identity module may import an OpenID
// Connect or JWT library; everyone else — including the mux — deals in the Subject below and never
// sees a token.
package auth

import "context"

// Subject is an authenticated caller, reduced to what the rest of the server needs.
//
// Deliberately not the token, and deliberately not the whole set of claims. A handler that can
// read the raw token can also be tempted to re-verify it, or to forward it somewhere; a handler
// that has a Subject can only ask who this is and what they may do.
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
// Implemented by the identity module and published on the kernel. It is optional: with no verifier
// registered the server runs unauthenticated, which is what makes the boilerplate startable before
// anyone has configured identity.
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
// Handlers that require a caller should treat false as errs.Unauthenticated rather than falling
// back to a default: an endpoint that quietly serves anonymous requests is the kind of thing that
// only gets noticed from the outside.
func SubjectFrom(ctx context.Context) (Subject, bool) {
	subject, ok := ctx.Value(subjectKey{}).(Subject)
	return subject, ok
}

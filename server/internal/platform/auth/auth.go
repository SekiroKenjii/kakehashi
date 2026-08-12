// Package auth declares who the caller is, and the contract for finding out.
//
// The contract lives here rather than in the identity module because the kernel installs the
// authentication middleware, and the kernel may not import a module. Declaring it in the platform
// lets identity implement it and the mux pick it up without anyone importing what it may not.
//
// It also keeps the fence in tools/archlint honest: only the identity module may import an OpenID
// Connect or JWT library; everyone else deals in Subject and never sees a token.
package auth

import "context"

// Subject is an authenticated caller, reduced to what the rest of the server needs.
//
// Deliberately not the token and not the whole claim set: a handler holding the raw token can be
// tempted to re-verify or forward it, while one holding a Subject can only ask who this is and
// what they may do.
type Subject struct {
	// ID comes from the token's subject claim.
	ID string

	// For display and audit. Either may be empty.
	Email string
	Name  string

	// SessionID says which sign-in this request belongs to, when the verifier knows. A correlation
	// id only; nothing may treat it as more.
	SessionID string
}

// Implemented by the identity module and published on the kernel. Optional: with no verifier
// registered the server runs unauthenticated, which is what makes the boilerplate startable before
// identity is configured.
type Verifier interface {
	// Verify must check signature, issuer, audience and expiry, and must not trust any claim it
	// has not verified.
	Verify(ctx context.Context, token string) (Subject, error)
}

// Unexported so nothing outside this package can write a Subject into a context: the only way one
// gets in is through the middleware that verified it.
type subjectKey struct{}

func WithSubject(ctx context.Context, subject Subject) context.Context {
	return context.WithValue(ctx, subjectKey{}, subject)
}

// A handler that requires a caller must treat false as errs.Unauthenticated rather than falling
// back to a default: an endpoint that quietly serves anonymous requests only gets noticed from
// the outside.
func SubjectFrom(ctx context.Context) (Subject, bool) {
	subject, ok := ctx.Value(subjectKey{}).(Subject)
	return subject, ok
}

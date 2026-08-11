// Package errs gives the server a small, closed set of error kinds.
//
// The point is the boundary between a module and the wire. A service returns an error; something
// has to decide whether the caller sees 404, 400, 409 or a bare 500, and it should not have to
// string-match or type-assert its way to that decision.
//
// That translation deliberately does not live here. This package imports nothing but the standard
// library, which is what lets domain/ import it without dragging a transport dependency into the
// innermost layer. The mapping from Kind to a Connect status code lives in the interceptor at
// internal/app/server, where the wire already is.
package errs

import (
	"errors"
	"fmt"
)

// Kind classifies an error well enough for the transport to react to it.
type Kind int

const (
	// Internal is the zero value on purpose: an error that nobody bothered to classify is one the
	// caller cannot act on, and a bare 500 is the honest thing to return for it.
	Internal Kind = iota

	// NotFound: the thing asked for does not exist.
	NotFound

	// Invalid: the caller supplied something the domain rejects. The message is safe, and meant,
	// to be shown to a user.
	Invalid

	// Conflict: the operation collides with existing state, e.g. a duplicate.
	Conflict

	// Unauthenticated: no credentials, or credentials that did not verify.
	Unauthenticated

	// Forbidden: the caller is known, and is not allowed to do this.
	//
	// Kept distinct from Unauthenticated because the remedies differ: one is fixed by signing in,
	// the other never is, and collapsing them sends users round a login loop that cannot succeed.
	Forbidden
)

func (k Kind) String() string {
	switch k {
	case NotFound:
		return "not_found"
	case Invalid:
		return "invalid"
	case Conflict:
		return "conflict"
	case Unauthenticated:
		return "unauthenticated"
	case Forbidden:
		return "forbidden"
	default:
		return "internal"
	}
}

// Error carries a Kind alongside the usual message and cause.
type Error struct {
	Kind Kind
	Msg  string
	Err  error
}

func (e *Error) Error() string {
	if e.Err == nil {
		return e.Msg
	}
	return fmt.Sprintf("%s: %v", e.Msg, e.Err)
}

func (e *Error) Unwrap() error { return e.Err }

func NotFoundf(format string, a ...any) *Error {
	return &Error{Kind: NotFound, Msg: fmt.Sprintf(format, a...)}
}

// Invalidf builds an Invalid error. Write the message for the user, not the log: it is one of the
// kinds whose text crosses the wire verbatim.
func Invalidf(format string, a ...any) *Error {
	return &Error{Kind: Invalid, Msg: fmt.Sprintf(format, a...)}
}

func Conflictf(format string, a ...any) *Error {
	return &Error{Kind: Conflict, Msg: fmt.Sprintf(format, a...)}
}

func Unauthenticatedf(format string, a ...any) *Error {
	return &Error{Kind: Unauthenticated, Msg: fmt.Sprintf(format, a...)}
}

func Forbiddenf(format string, a ...any) *Error {
	return &Error{Kind: Forbidden, Msg: fmt.Sprintf(format, a...)}
}

// Internalf wraps cause as an Internal error.
func Internalf(cause error, format string, a ...any) *Error {
	return &Error{Kind: Internal, Msg: fmt.Sprintf(format, a...), Err: cause}
}

// KindOf reports the Kind of err, unwrapping as it goes. Anything that is not an *Error is
// Internal, including nil, which no caller should be asking about.
func KindOf(err error) Kind {
	var e *Error
	if errors.As(err, &e) {
		return e.Kind
	}
	return Internal
}

// PublicMessage returns a message safe to send to a caller.
//
// Internal errors deliberately collapse to a fixed string. Their text is written for whoever reads
// the log and tends to carry connection strings, SQL and driver noise: nothing the caller can act
// on, and rather more than we meant to tell them. On a server that is reachable from the internet
// this is not tidiness, it is the difference between a stack trace staying in your logs and it
// being handed to whoever asked.
func PublicMessage(err error) string {
	if err == nil {
		return ""
	}
	var e *Error
	if errors.As(err, &e) && e.Kind != Internal {
		return e.Msg
	}
	return "Something went wrong."
}

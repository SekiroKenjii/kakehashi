package auth

import "context"

// Scope is how much of a table a permission reaches.
//
// This is the row-level half of authorization, and it lives on the grant rather than beside it: a
// permission and the rows it covers are one decision, and splitting them across two systems is how
// they drift.
type Scope string

// The scopes, widest first. The ordering is load-bearing — see Widest.
const (
	// ScopeNone is the absence of a grant. Not a value anyone stores; it is what Grants returns
	// for a permission the caller does not hold.
	ScopeNone Scope = ""

	// ScopeOwn covers rows the caller owns.
	ScopeOwn Scope = "own"

	// ScopeTeam covers rows owned by anyone sharing the caller's team.
	ScopeTeam Scope = "team"

	// ScopeAll covers every row.
	ScopeAll Scope = "all"
)

// rank orders the scopes so two grants can be merged. Unknown values rank below everything, so a
// scope this build does not recognise narrows rather than widens.
func (s Scope) rank() int {
	switch s {
	case ScopeAll:
		return 3
	case ScopeTeam:
		return 2
	case ScopeOwn:
		return 1
	default:
		return 0
	}
}

// Widest returns whichever of two scopes reaches further.
//
// Roles combine by widening, never by narrowing. A system where adding a role can take access away
// is a system where nobody can predict what a role does, and the first person to discover it is a
// user who lost something.
func Widest(a, b Scope) Scope {
	if b.rank() > a.rank() {
		return b
	}
	return a
}

// Grants is what one caller may do: permission key to the widest scope their roles give them.
//
// Absent key means no grant. That is why lookups go through Scope and Allows rather than indexing
// the map directly — a missing key and an unknown scope must read the same way.
type Grants map[string]Scope

// Scope returns how far this caller's grant on permission reaches, or ScopeNone.
func (g Grants) Scope(permission string) Scope {
	if g == nil {
		return ScopeNone
	}
	return g[permission]
}

// Allows reports whether the caller holds the permission at all, at any scope.
func (g Grants) Allows(permission string) bool {
	return g.Scope(permission) != ScopeNone
}

// ModuleAccess is the permission key that gates a module's routes.
//
// One home for the format, because three places name it: the route gate that enforces it, the
// authorization module that mints it into its catalogue, and the navigation module that falls back
// to it for a destination declaring no permission of its own. Two of those are modules and cannot
// see each other, so the platform holds the string they must agree on.
func ModuleAccess(moduleID string) string {
	return moduleID + ".access"
}

// Permissions resolves what a caller may do.
//
// Declared here and implemented by a module, exactly as Verifier is, because the mux has to ask on
// every request and the platform may not import a module.
//
// Resolved per request rather than read from the token, deliberately. An access token lives
// minutes; a permission revoked a minute ago must not keep working for the rest of them. The token
// still carries a roles claim for display, and nothing authorizes on it.
type Permissions interface {
	Resolve(ctx context.Context, subject Subject) (Grants, error)
}

type grantsKey struct{}

// WithGrants puts a caller's resolved grants on the context. The route gate does this once, so a
// handler that needs a finer permission than the gate checked pays no second query.
func WithGrants(ctx context.Context, grants Grants) context.Context {
	return context.WithValue(ctx, grantsKey{}, grants)
}

// GrantsFrom returns the grants the gate resolved, or nil when the request was not gated — an
// ungated route, or a server with no authorization module mounted.
func GrantsFrom(ctx context.Context) Grants {
	grants, _ := ctx.Value(grantsKey{}).(Grants)
	return grants
}

// ScopeOf is the shorthand a store uses to narrow its own query.
//
// The filter belongs to the store rather than the gate: a gate that rewrote everyone's SQL would
// have to understand everyone's schema, while a store narrowing its own query only has to
// understand its own.
func ScopeOf(ctx context.Context, permission string) Scope {
	return GrantsFrom(ctx).Scope(permission)
}

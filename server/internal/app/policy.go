package app

import "github.com/SekiroKenjii/kakehashi/server/internal/platform/auth"

// PolicyKind is what a route checks before its handler runs.
//
// The zero value is not a policy, it is the ABSENCE of one. Routes refuses to collect a route still
// carrying it, so "I forgot" is a failed boot rather than an open endpoint.
type PolicyKind uint8

const (
	// PolicyUnset is the zero value: a Route whose Policy was never assigned.
	PolicyUnset PolicyKind = iota

	// PolicyPublic serves anyone, verified or anonymous.
	PolicyPublic

	// PolicySignedIn serves any caller the verifier authenticated, and checks no permission.
	PolicySignedIn

	// PolicyModuleAccess requires the contributing module's <id>.access.
	PolicyModuleAccess

	// PolicyPermission requires one named permission.
	PolicyPermission
)

// RoutePolicy is what a caller must be before a route's handler runs.
//
// It is mandatory. Every route states its policy beside its pattern, and boot refuses the ones that
// do not. Why the unit is the route rather than the module:
// docs/adr/0001-per-route-permission-policy.md.
//
// The fields are unexported and there is no exported literal form, so the only non-zero values come
// from the four constructors below. That stops an accidental half-built policy, not a deliberate
// one: PolicyPublic still costs one exported call inside a module's own file, and the documented way
// to add a module is to copy an existing one. So the composition root names the modules permitted
// to make that call — see Kernel.AllowUnprotectedRoutes. Granularity lives at the route; review
// salience at the root.
//
// A policy covers everything its handler can reach. Two shapes here are whole routers — the OpenID
// Connect provider mounted at "/", and each Connect service, which is one route and N procedures
// behind it — so "per route" means "per mounted pattern". A finer check inside a handler is an
// addition to the policy, never a substitute for it.
type RoutePolicy struct {
	kind PolicyKind
	key  string
}

// Public serves anyone, signed in or not.
//
// Reserve it for what must answer before anybody can sign in: the liveness probe, the OpenID Connect
// surface, the sign-in endpoints themselves.
func Public() RoutePolicy { return RoutePolicy{kind: PolicyPublic} }

// SignedIn requires a verified caller and checks no permission.
//
// For the endpoints that are about the caller's own account or the caller's own view — reading your
// own profile, asking what you may do, asking what your navigation pane looks like. A permission
// guarding your own profile would be a permission somebody could take away, leaving an account that
// can sign in and then do nothing.
func SignedIn() RoutePolicy { return RoutePolicy{kind: PolicySignedIn} }

// ModuleAccess requires the contributing module's <id>.access. The ordinary case for a feature.
func ModuleAccess() RoutePolicy { return RoutePolicy{kind: PolicyModuleAccess} }

// Permission requires one named permission — an administrative surface, usually.
func Permission(key string) RoutePolicy {
	return RoutePolicy{kind: PolicyPermission, key: key}
}

// Kind reports which policy this is. PolicyUnset means the route never declared one.
func (p RoutePolicy) Kind() PolicyKind { return p.kind }

// Unprotected reports whether this policy lets a caller through without holding any permission.
//
// Public and SignedIn both qualify: neither consults the grants. It is the question the composition
// root's exemption list is asked about every route.
func (p RoutePolicy) Unprotected() bool {
	return p.kind == PolicyPublic || p.kind == PolicySignedIn
}

// PermissionFor returns the permission key this policy requires, or "" when it requires none.
//
// moduleID is the contributing module, which the kernel stamps rather than the module naming itself.
// That matters here: this is the value that decides whose permission applies.
func (p RoutePolicy) PermissionFor(moduleID string) string {
	switch p.kind {
	case PolicyModuleAccess:
		return auth.ModuleAccess(moduleID)
	case PolicyPermission:
		return p.key
	default:
		return ""
	}
}

// String names the policy, for the boot log and for the panic message.
func (p RoutePolicy) String() string {
	switch p.kind {
	case PolicyPublic:
		return "public"
	case PolicySignedIn:
		return "signed-in"
	case PolicyModuleAccess:
		return "module-access"
	case PolicyPermission:
		return "permission:" + p.key
	default:
		return "unset"
	}
}

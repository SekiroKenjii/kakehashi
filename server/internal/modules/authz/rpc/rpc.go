// Package rpc is the authorization module's wire layer.
//
// The only package in the module allowed to import the generated protobuf code, and tools/archlint
// enforces that. Everything here is mapping, except reading the caller off the context — who is
// asking is not a field a client may send, and this is the layer where the request still exists to
// be asked.
package rpc

import (
	"context"
	"net/http"

	"connectrpc.com/connect"

	authzv1 "github.com/SekiroKenjii/kakehashi/server/internal/gen/kakehashi/authz/v1"
	"github.com/SekiroKenjii/kakehashi/server/internal/gen/kakehashi/authz/v1/authzv1connect"
	"github.com/SekiroKenjii/kakehashi/server/internal/modules/authz/domain"
	"github.com/SekiroKenjii/kakehashi/server/internal/platform/auth"
	"github.com/SekiroKenjii/kakehashi/server/internal/platform/errs"
)

func NewRoute(opts []connect.HandlerOption) (string, http.Handler) {
	return authzv1connect.NewAuthzServiceHandler(&handler{}, opts...)
}

// Holds no service: the grants were resolved by the middleware and are on the context already.
type handler struct{}

// Reads the resolved grants rather than querying, so this endpoint and the gate that refuses a
// request look at the identical answer. Two code paths that could disagree about what a caller may
// do is how a client ends up drawing an unlocked door onto a locked room.
func (h *handler) ListMyGrants(
	ctx context.Context, _ *connect.Request[authzv1.ListMyGrantsRequest],
) (*connect.Response[authzv1.ListMyGrantsResponse], error) {
	if _, ok := auth.SubjectFrom(ctx); !ok {
		// Unauthenticated rather than an empty list: an account that may do nothing and an expired
		// token draw the same screen and mean opposite things, and only one is fixed by signing in
		// again.
		return nil, errs.Unauthenticatedf("Sign in to see your permissions.")
	}

	grants := auth.GrantsFrom(ctx)
	out := make([]*authzv1.Grant, 0, len(grants))
	for key, scope := range grants {
		out = append(out, &authzv1.Grant{PermissionKey: key, Scope: toProtoScope(string(scope))})
	}
	return connect.NewResponse(&authzv1.ListMyGrantsResponse{Grants: out}), nil
}

// An unrecognised scope becomes UNSPECIFIED, which the client reads as "no reach" — narrowing, the
// same direction platform/auth widens from.
func toProtoScope(scope string) authzv1.Scope {
	switch scope {
	case domain.ScopeOwn:
		return authzv1.Scope_SCOPE_OWN
	case domain.ScopeTeam:
		return authzv1.Scope_SCOPE_TEAM
	case domain.ScopeAll:
		return authzv1.Scope_SCOPE_ALL
	default:
		return authzv1.Scope_SCOPE_UNSPECIFIED
	}
}

// UNSPECIFIED becomes the empty string, which domain.IsScope rejects — a client that forgot to set
// the scope is told so rather than silently given "own".
func fromProtoScope(scope authzv1.Scope) string {
	switch scope {
	case authzv1.Scope_SCOPE_OWN:
		return domain.ScopeOwn
	case authzv1.Scope_SCOPE_TEAM:
		return domain.ScopeTeam
	case authzv1.Scope_SCOPE_ALL:
		return domain.ScopeAll
	default:
		return ""
	}
}

var _ authzv1connect.AuthzServiceHandler = (*handler)(nil)

package rpc

import (
	"context"
	"net/http"

	"connectrpc.com/connect"
	"google.golang.org/protobuf/types/known/timestamppb"

	authzv1 "__GO_MODULE__/server/internal/gen/__PROTO_PACKAGE__/authz/v1"
	"__GO_MODULE__/server/internal/gen/__PROTO_PACKAGE__/authz/v1/authzv1connect"
	authzapi "__GO_MODULE__/server/internal/modules/authz/api"
	"__GO_MODULE__/server/internal/modules/authz/service"
	"__GO_MODULE__/server/internal/platform/auth"
	"__GO_MODULE__/server/internal/platform/errs"
)

// The administrator's surface. A separate route from the one above, because the module wraps this
// path in a permission check and that wrapper is what protects every procedure added here later.

// NewAdminRoute builds the Connect handler for AuthzAdminService.
func NewAdminRoute(svc *service.Service, opts []connect.HandlerOption) (string, http.Handler) {
	return authzv1connect.NewAuthzAdminServiceHandler(&adminHandler{svc: svc}, opts...)
}

type adminHandler struct {
	svc *service.Service
}

func (h *adminHandler) ListRoles(
	ctx context.Context, _ *connect.Request[authzv1.ListRolesRequest],
) (*connect.Response[authzv1.ListRolesResponse], error) {
	roles, total, err := h.svc.Roles(ctx)
	if err != nil {
		return nil, err
	}

	out := make([]*authzv1.Role, len(roles))
	for i, r := range roles {
		out[i] = &authzv1.Role{
			Id:              r.Role.ID,
			Name:            r.Role.Name,
			Description:     r.Role.Description,
			IsSystem:        r.Role.IsSystem,
			PermissionCount: int32(r.PermissionCount),
			AccountCount:    int32(r.AccountCount),
		}
	}
	return connect.NewResponse(&authzv1.ListRolesResponse{
		Roles: out, PermissionTotal: int32(total),
	}), nil
}

func (h *adminHandler) ListPermissions(
	ctx context.Context, _ *connect.Request[authzv1.ListPermissionsRequest],
) (*connect.Response[authzv1.ListPermissionsResponse], error) {
	permissions, err := h.svc.Permissions(ctx)
	if err != nil {
		return nil, err
	}

	out := make([]*authzv1.Permission, len(permissions))
	for i, p := range permissions {
		out[i] = &authzv1.Permission{
			Key:         p.Key,
			Name:        p.Name,
			Description: p.Description,
			Category:    p.Category,
			IsHighRisk:  p.IsHighRisk,
			IsScoped:    p.IsScoped,
		}
	}
	return connect.NewResponse(&authzv1.ListPermissionsResponse{Permissions: out}), nil
}

func (h *adminHandler) GetRoleGrants(
	ctx context.Context, req *connect.Request[authzv1.GetRoleGrantsRequest],
) (*connect.Response[authzv1.GetRoleGrantsResponse], error) {
	grants, err := h.svc.RoleGrants(ctx, req.Msg.GetRoleId())
	if err != nil {
		return nil, err
	}
	return connect.NewResponse(&authzv1.GetRoleGrantsResponse{Grants: toProtoGrants(grants)}), nil
}

func (h *adminHandler) SaveRoleGrants(
	ctx context.Context, req *connect.Request[authzv1.SaveRoleGrantsRequest],
) (*connect.Response[authzv1.SaveRoleGrantsResponse], error) {
	actor, err := actorFrom(ctx)
	if err != nil {
		return nil, err
	}

	wanted := make([]authzapi.Grant, 0, len(req.Msg.GetGrants()))
	for _, g := range req.Msg.GetGrants() {
		wanted = append(wanted, authzapi.Grant{
			PermissionKey: g.GetPermissionKey(),
			Scope:         fromProtoScope(g.GetScope()),
		})
	}

	result, err := h.svc.SaveRoleGrants(ctx, req.Msg.GetRoleId(), wanted, actor)
	if err != nil {
		return nil, err
	}
	return connect.NewResponse(&authzv1.SaveRoleGrantsResponse{
		Granted:  int32(result.Granted),
		Revoked:  int32(result.Revoked),
		Rescoped: int32(result.Rescoped),
	}), nil
}

func (h *adminHandler) CreateRole(
	ctx context.Context, req *connect.Request[authzv1.CreateRoleRequest],
) (*connect.Response[authzv1.CreateRoleResponse], error) {
	actor, err := actorFrom(ctx)
	if err != nil {
		return nil, err
	}

	role, err := h.svc.CreateRole(
		ctx, req.Msg.GetName(), req.Msg.GetDescription(), req.Msg.GetCloneFromRoleId(), actor)
	if err != nil {
		return nil, err
	}
	return connect.NewResponse(&authzv1.CreateRoleResponse{
		Role: &authzv1.Role{
			Id: role.ID, Name: role.Name, Description: role.Description, IsSystem: role.IsSystem,
		},
	}), nil
}

func (h *adminHandler) UpdateRole(
	ctx context.Context, req *connect.Request[authzv1.UpdateRoleRequest],
) (*connect.Response[authzv1.UpdateRoleResponse], error) {
	actor, err := actorFrom(ctx)
	if err != nil {
		return nil, err
	}

	role, err := h.svc.UpdateRole(
		ctx, req.Msg.GetRoleId(), req.Msg.GetName(), req.Msg.GetDescription(), actor)
	if err != nil {
		return nil, err
	}
	return connect.NewResponse(&authzv1.UpdateRoleResponse{
		Role: &authzv1.Role{
			Id: role.ID, Name: role.Name, Description: role.Description, IsSystem: role.IsSystem,
		},
	}), nil
}

func (h *adminHandler) DeleteRole(
	ctx context.Context, req *connect.Request[authzv1.DeleteRoleRequest],
) (*connect.Response[authzv1.DeleteRoleResponse], error) {
	actor, err := actorFrom(ctx)
	if err != nil {
		return nil, err
	}
	if err := h.svc.DeleteRole(ctx, req.Msg.GetRoleId(), actor); err != nil {
		return nil, err
	}
	return connect.NewResponse(&authzv1.DeleteRoleResponse{}), nil
}

func (h *adminHandler) AssignRole(
	ctx context.Context, req *connect.Request[authzv1.AssignRoleRequest],
) (*connect.Response[authzv1.AssignRoleResponse], error) {
	actor, err := actorFrom(ctx)
	if err != nil {
		return nil, err
	}
	if err := h.svc.AssignRole(ctx, req.Msg.GetEmail(), req.Msg.GetRoleId(), actor); err != nil {
		return nil, err
	}
	return connect.NewResponse(&authzv1.AssignRoleResponse{}), nil
}

func (h *adminHandler) UnassignRole(
	ctx context.Context, req *connect.Request[authzv1.UnassignRoleRequest],
) (*connect.Response[authzv1.UnassignRoleResponse], error) {
	actor, err := actorFrom(ctx)
	if err != nil {
		return nil, err
	}
	if err := h.svc.UnassignRole(ctx, req.Msg.GetEmail(), req.Msg.GetRoleId(), actor); err != nil {
		return nil, err
	}
	return connect.NewResponse(&authzv1.UnassignRoleResponse{}), nil
}

func (h *adminHandler) ListAccountRoles(
	ctx context.Context, req *connect.Request[authzv1.ListAccountRolesRequest],
) (*connect.Response[authzv1.ListAccountRolesResponse], error) {
	byAccount, err := h.svc.RolesOfAccounts(ctx, req.Msg.GetAccountIds())
	if err != nil {
		return nil, err
	}

	out := make([]*authzv1.AccountRoles, 0, len(byAccount))
	for id, roles := range byAccount {
		entry := &authzv1.AccountRoles{AccountId: id, Roles: make([]*authzv1.Role, len(roles))}
		for i, r := range roles {
			entry.Roles[i] = &authzv1.Role{
				Id: r.ID, Name: r.Name, Description: r.Description, IsSystem: r.IsSystem,
			}
		}
		out = append(out, entry)
	}
	return connect.NewResponse(&authzv1.ListAccountRolesResponse{Accounts: out}), nil
}

// ListAuditEntries needs audit.view on top of the roles.manage the route already required.
//
// The one hand-written check in this file, and it is here rather than on the route because the
// route is the service and a second Connect service for one procedure is more machinery than the
// three lines below.
func (h *adminHandler) ListAuditEntries(
	ctx context.Context, req *connect.Request[authzv1.ListAuditEntriesRequest],
) (*connect.Response[authzv1.ListAuditEntriesResponse], error) {
	if !auth.GrantsFrom(ctx).Allows(authzapi.PermissionViewAudit) {
		return nil, errs.Forbiddenf("Viewing the audit trail requires %s.",
			authzapi.PermissionViewAudit)
	}

	entries, err := h.svc.AuditEntries(ctx, int(req.Msg.GetPageSize()))
	if err != nil {
		return nil, err
	}

	out := make([]*authzv1.AuditEntry, len(entries))
	for i, e := range entries {
		out[i] = &authzv1.AuditEntry{
			Id:            e.ID,
			OccurredAt:    timestamppb.New(e.OccurredAt),
			ActorId:       e.ActorID,
			ActorName:     e.ActorName,
			Action:        e.Action,
			RoleId:        e.RoleID,
			RoleName:      e.RoleName,
			PermissionKey: e.PermissionKey,
			Detail:        e.Detail,
		}
	}
	return connect.NewResponse(&authzv1.ListAuditEntriesResponse{Entries: out}), nil
}

func toProtoGrants(grants []authzapi.Grant) []*authzv1.Grant {
	out := make([]*authzv1.Grant, len(grants))
	for i, g := range grants {
		out[i] = &authzv1.Grant{PermissionKey: g.PermissionKey, Scope: toProtoScope(g.Scope)}
	}
	return out
}

// actorFrom reads who is making the change.
//
// Off the context, never off the request: an actor a client could send is an audit trail a client
// could forge, and the whole value of the trail is that it cannot be.
func actorFrom(ctx context.Context) (service.Actor, error) {
	subject, ok := auth.SubjectFrom(ctx)
	if !ok {
		return service.Actor{}, errs.Unauthenticatedf("Sign in to change access.")
	}

	// The address stands in when the token carries no name. When it carries neither, the name is
	// left empty on purpose: the service looks it up, which it can do and this cannot.
	name := subject.Name
	if name == "" {
		name = subject.Email
	}
	return service.Actor{ID: subject.ID, Name: name}, nil
}

var _ authzv1connect.AuthzAdminServiceHandler = (*adminHandler)(nil)

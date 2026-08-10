package rpc

import (
	"context"
	"net/http"

	"connectrpc.com/connect"

	navigationv1 "github.com/SekiroKenjii/kakehashi/server/internal/gen/kakehashi/navigation/v1"
	"github.com/SekiroKenjii/kakehashi/server/internal/gen/kakehashi/navigation/v1/navigationv1connect"
	"github.com/SekiroKenjii/kakehashi/server/internal/modules/navigation/domain"
	"github.com/SekiroKenjii/kakehashi/server/internal/modules/navigation/service"
)

// NewAdminRoute builds the Connect handler for NavigationAdminService.
//
// No permission check anywhere in this file, and that is not an omission: the module wraps this whole
// route in one, so every procedure added here later inherits it without anyone remembering to. A
// hand-written check per handler is a check somebody eventually forgets.
func NewAdminRoute(svc *service.Service, opts []connect.HandlerOption) (string, http.Handler) {
	return navigationv1connect.NewNavigationAdminServiceHandler(&adminHandler{svc: svc}, opts...)
}

type adminHandler struct {
	svc *service.Service
}

func (h *adminHandler) ListGroups(
	ctx context.Context, _ *connect.Request[navigationv1.ListGroupsRequest],
) (*connect.Response[navigationv1.ListGroupsResponse], error) {
	groups, err := h.svc.Groups(ctx)
	if err != nil {
		return nil, err
	}

	out := make([]*navigationv1.Group, 0, len(groups))
	for _, group := range groups {
		out = append(out, toGroup(group))
	}
	return connect.NewResponse(&navigationv1.ListGroupsResponse{Groups: out}), nil
}

func (h *adminHandler) CreateGroup(
	ctx context.Context, req *connect.Request[navigationv1.CreateGroupRequest],
) (*connect.Response[navigationv1.CreateGroupResponse], error) {
	group, err := h.svc.CreateGroup(
		ctx, req.Msg.GetId(), req.Msg.GetTitle(), int(req.Msg.GetSortOrder()))
	if err != nil {
		return nil, err
	}
	return connect.NewResponse(&navigationv1.CreateGroupResponse{Group: toGroup(group)}), nil
}

func (h *adminHandler) UpdateGroup(
	ctx context.Context, req *connect.Request[navigationv1.UpdateGroupRequest],
) (*connect.Response[navigationv1.UpdateGroupResponse], error) {
	group, err := h.svc.UpdateGroup(
		ctx, req.Msg.GetId(), req.Msg.GetTitle(), int(req.Msg.GetSortOrder()))
	if err != nil {
		return nil, err
	}
	return connect.NewResponse(&navigationv1.UpdateGroupResponse{Group: toGroup(group)}), nil
}

func (h *adminHandler) DeleteGroup(
	ctx context.Context, req *connect.Request[navigationv1.DeleteGroupRequest],
) (*connect.Response[navigationv1.DeleteGroupResponse], error) {
	if err := h.svc.DeleteGroup(ctx, req.Msg.GetId()); err != nil {
		return nil, err
	}
	return connect.NewResponse(&navigationv1.DeleteGroupResponse{}), nil
}

func (h *adminHandler) ListItems(
	ctx context.Context, _ *connect.Request[navigationv1.ListItemsRequest],
) (*connect.Response[navigationv1.ListItemsResponse], error) {
	items, err := h.svc.Items(ctx)
	if err != nil {
		return nil, err
	}

	out := make([]*navigationv1.ItemConfig, 0, len(items))
	for _, item := range items {
		out = append(out, toItemConfig(item))
	}
	return connect.NewResponse(&navigationv1.ListItemsResponse{Items: out}), nil
}

func (h *adminHandler) MoveItem(
	ctx context.Context, req *connect.Request[navigationv1.MoveItemRequest],
) (*connect.Response[navigationv1.MoveItemResponse], error) {
	item, err := h.svc.MoveItem(
		ctx, req.Msg.GetId(), req.Msg.GetGroupId(), int(req.Msg.GetSortOrder()))
	if err != nil {
		return nil, err
	}
	return connect.NewResponse(&navigationv1.MoveItemResponse{Item: toItemConfig(item)}), nil
}

func (h *adminHandler) UpdateItem(
	ctx context.Context, req *connect.Request[navigationv1.UpdateItemRequest],
) (*connect.Response[navigationv1.UpdateItemResponse], error) {
	item, err := h.svc.UpdateItem(
		ctx, req.Msg.GetId(), req.Msg.GetTitle(), req.Msg.GetIcon(), req.Msg.GetIsVisible())
	if err != nil {
		return nil, err
	}
	return connect.NewResponse(&navigationv1.UpdateItemResponse{Item: toItemConfig(item)}), nil
}

func toGroup(g domain.Group) *navigationv1.Group {
	return &navigationv1.Group{
		Id:        g.ID,
		Title:     g.Title,
		SortOrder: int32(g.Order),
		IsSystem:  g.IsSystem,
	}
}

func toItemConfig(item service.ItemConfig) *navigationv1.ItemConfig {
	return &navigationv1.ItemConfig{
		Id:                 item.DestinationID,
		ModuleId:           item.ModuleID,
		GroupId:            item.GroupID,
		Title:              item.Title,
		Icon:               item.Icon,
		DefaultTitle:       item.DefaultTitle,
		DefaultIcon:        item.DefaultIcon,
		SortOrder:          int32(item.Order),
		IsVisible:          item.IsVisible,
		IsOrphan:           item.Orphan,
		RequiredPermission: item.RequiredPermission,
		HideWhenDenied:     item.HideWhenDenied,
	}
}

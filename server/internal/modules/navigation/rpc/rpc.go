// Package rpc is the only package in the module allowed to import the generated protobuf code, and
// tools/archlint enforces that. Mapping only, plus the one thing that cannot be mapped: who is
// asking, read off the context because a caller is not a field a client may send.
package rpc

import (
	"context"
	"net/http"

	"connectrpc.com/connect"

	navigationv1 "github.com/SekiroKenjii/kakehashi/server/internal/gen/kakehashi/navigation/v1"
	"github.com/SekiroKenjii/kakehashi/server/internal/gen/kakehashi/navigation/v1/navigationv1connect"
	"github.com/SekiroKenjii/kakehashi/server/internal/modules/navigation/service"
	"github.com/SekiroKenjii/kakehashi/server/internal/platform/auth"
	"github.com/SekiroKenjii/kakehashi/server/internal/platform/errs"
)

func NewRoute(svc *service.Service, opts []connect.HandlerOption) (string, http.Handler) {
	return navigationv1connect.NewNavigationServiceHandler(&handler{svc: svc}, opts...)
}

type handler struct {
	svc *service.Service
}

// Open to any signed-in caller, and it has to be: this is the answer a client needs before it can
// draw anything at all, so gating it on a permission would mean a new account with no grants gets
// no pane and no way to ask for one.
//
// It reads the grants the gate already resolved onto the request rather than querying again. Two
// code paths that could disagree about what a caller may do is how a client ends up drawing an
// unlocked door onto a locked room.
func (h *handler) GetNavigation(
	ctx context.Context, _ *connect.Request[navigationv1.GetNavigationRequest],
) (*connect.Response[navigationv1.GetNavigationResponse], error) {
	if _, ok := auth.SubjectFrom(ctx); !ok {
		// Unauthenticated rather than the pane an anonymous caller would get. An account that may
		// reach nothing and an expired token draw the same empty pane and mean opposite things;
		// only one of them is fixed by signing in again.
		return nil, errs.Unauthenticatedf("Sign in to see your navigation.")
	}

	pane, err := h.svc.Build(ctx, auth.GrantsFrom(ctx))
	if err != nil {
		return nil, err
	}

	out := &navigationv1.GetNavigationResponse{
		Ungrouped: toItems(pane.Ungrouped),
		Groups:    make([]*navigationv1.GroupedItems, 0, len(pane.Groups)),
	}
	for _, group := range pane.Groups {
		out.Groups = append(out.Groups, &navigationv1.GroupedItems{
			GroupId: group.GroupID,
			Title:   group.Title,
			Items:   toItems(group.Items),
		})
	}
	return connect.NewResponse(out), nil
}

func toItems(items []service.Item) []*navigationv1.Item {
	out := make([]*navigationv1.Item, 0, len(items))
	for _, item := range items {
		out = append(out, &navigationv1.Item{
			Id:       item.ID,
			ModuleId: item.ModuleID,
			Title:    item.Title,
			Icon:     item.Icon,
			Enabled:  item.Enabled,
		})
	}
	return out
}

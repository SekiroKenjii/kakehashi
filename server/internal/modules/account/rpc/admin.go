// The administrator's surface, over Connect rather than the plain JSON the /account/* endpoints
// use. The two are different audiences: those seven serve a caller acting on their own record and
// answer the client's AccountGateway, this one serves somebody acting on everybody else's and
// answers a screen. Different audience, different contract, and a separate route — the unit the
// server puts a permission check on.

package rpc

import (
	"context"
	"net/http"
	"time"

	"connectrpc.com/connect"
	"google.golang.org/protobuf/types/known/timestamppb"

	accountv1 "__GO_MODULE__/server/internal/gen/__PROTO_PACKAGE__/account/v1"
	"__GO_MODULE__/server/internal/gen/__PROTO_PACKAGE__/account/v1/accountv1connect"
	accountapi "__GO_MODULE__/server/internal/modules/account/api"
	"__GO_MODULE__/server/internal/modules/account/service"
	"__GO_MODULE__/server/internal/platform/auth"
	"__GO_MODULE__/server/internal/platform/errs"
)

// NewAdminRoute builds the Connect handler for AccountAdminService.
func NewAdminRoute(svc *service.Service, opts []connect.HandlerOption) (string, http.Handler) {
	return accountv1connect.NewAccountAdminServiceHandler(&adminHandler{svc: svc}, opts...)
}

type adminHandler struct {
	svc *service.Service
}

func (h *adminHandler) ListAccounts(
	ctx context.Context, _ *connect.Request[accountv1.ListAccountsRequest],
) (*connect.Response[accountv1.ListAccountsResponse], error) {
	accounts, err := h.svc.Accounts(ctx)
	if err != nil {
		return nil, err
	}

	out := make([]*accountv1.Account, len(accounts))
	for i, a := range accounts {
		out[i] = toProto(a)
	}
	return connect.NewResponse(&accountv1.ListAccountsResponse{Accounts: out}), nil
}

func toProto(a accountapi.Account) *accountv1.Account {
	return &accountv1.Account{
		Id:                 a.ID,
		Email:              a.Email,
		DisplayName:        a.DisplayName,
		Phone:              a.Phone,
		TeamId:             a.TeamID,
		IsActive:           a.IsActive,
		LastSignInAt:       optionalTime(a.LastSignInAt),
		CreatedAt:          timestamppb.New(a.CreatedAt),
		ActiveSessionCount: int32(a.ActiveSessionCount),
	}
}

func (h *adminHandler) SetAccountActive(
	ctx context.Context, req *connect.Request[accountv1.SetAccountActiveRequest],
) (*connect.Response[accountv1.SetAccountActiveResponse], error) {
	subject, ok := auth.SubjectFrom(ctx)
	if !ok {
		return nil, errs.Unauthenticatedf("Sign in to change an account.")
	}

	err := h.svc.SetActive(ctx, req.Msg.GetAccountId(), req.Msg.GetIsActive(), subject.ID)
	if err != nil {
		return nil, err
	}
	return connect.NewResponse(&accountv1.SetAccountActiveResponse{}), nil
}

func (h *adminHandler) CreateAccount(
	ctx context.Context, req *connect.Request[accountv1.CreateAccountRequest],
) (*connect.Response[accountv1.CreateAccountResponse], error) {
	account, err := h.svc.CreateAccount(
		ctx, req.Msg.GetEmail(), req.Msg.GetDisplayName(), req.Msg.GetPassword())
	if err != nil {
		return nil, err
	}
	return connect.NewResponse(&accountv1.CreateAccountResponse{Account: toProto(account)}), nil
}

func (h *adminHandler) UpdateAccount(
	ctx context.Context, req *connect.Request[accountv1.UpdateAccountRequest],
) (*connect.Response[accountv1.UpdateAccountResponse], error) {
	account, err := h.svc.UpdateAccount(ctx, req.Msg.GetAccountId(), req.Msg.GetDisplayName(),
		req.Msg.GetPhone(), req.Msg.GetTeamId())
	if err != nil {
		return nil, err
	}
	return connect.NewResponse(&accountv1.UpdateAccountResponse{Account: toProto(account)}), nil
}

func (h *adminHandler) ResetPassword(
	ctx context.Context, req *connect.Request[accountv1.ResetPasswordRequest],
) (*connect.Response[accountv1.ResetPasswordResponse], error) {
	err := h.svc.ResetPassword(ctx, req.Msg.GetAccountId(), req.Msg.GetNewPassword())
	if err != nil {
		return nil, err
	}
	return connect.NewResponse(&accountv1.ResetPasswordResponse{}), nil
}

func (h *adminHandler) RevokeAccountSession(
	ctx context.Context, req *connect.Request[accountv1.RevokeAccountSessionRequest],
) (*connect.Response[accountv1.RevokeAccountSessionResponse], error) {
	err := h.svc.RevokeAccountSession(ctx, req.Msg.GetAccountId(), req.Msg.GetSessionId())
	if err != nil {
		return nil, err
	}
	return connect.NewResponse(&accountv1.RevokeAccountSessionResponse{}), nil
}

func (h *adminHandler) ListAccountSessions(
	ctx context.Context, req *connect.Request[accountv1.ListAccountSessionsRequest],
) (*connect.Response[accountv1.ListAccountSessionsResponse], error) {
	// The caller's own session id rides along so "this device" stays truthful when an
	// administrator inspects their own account. For anybody else's it matches nothing.
	subject, _ := auth.SubjectFrom(ctx)

	sessions, err := h.svc.AccountSessions(ctx, req.Msg.GetAccountId(), subject.SessionID)
	if err != nil {
		return nil, err
	}

	out := make([]*accountv1.Session, len(sessions))
	for i, sess := range sessions {
		out[i] = &accountv1.Session{
			Id:         sess.ID,
			Client:     sess.Client,
			Device:     sess.Device,
			IpAddress:  sess.IPAddress,
			CreatedAt:  timestamppb.New(sess.CreatedAt),
			LastSeenAt: timestamppb.New(sess.LastSeenAt),
			IsCurrent:  sess.IsCurrent,
		}
	}
	return connect.NewResponse(&accountv1.ListAccountSessionsResponse{Sessions: out}), nil
}

func (h *adminHandler) DeleteAccount(
	ctx context.Context, req *connect.Request[accountv1.DeleteAccountRequest],
) (*connect.Response[accountv1.DeleteAccountResponse], error) {
	subject, ok := auth.SubjectFrom(ctx)
	if !ok {
		return nil, errs.Unauthenticatedf("Sign in to change an account.")
	}

	if err := h.svc.DeleteAccount(ctx, req.Msg.GetAccountId(), subject.ID); err != nil {
		return nil, err
	}
	return connect.NewResponse(&accountv1.DeleteAccountResponse{}), nil
}

// optionalTime leaves the field unset for the zero time, which is how "never signed in" crosses
// the wire. A zero timestamp would arrive as 1970 and be rendered as one.
func optionalTime(t time.Time) *timestamppb.Timestamp {
	if t.IsZero() {
		return nil
	}
	return timestamppb.New(t)
}

var _ accountv1connect.AccountAdminServiceHandler = (*adminHandler)(nil)

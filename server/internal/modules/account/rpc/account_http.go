package rpc

import (
	"net/http"
	"strconv"
	"time"

	accountapi "__GO_MODULE__/server/internal/modules/account/api"
)

// accountHandler serves the JSON endpoints behind the desktop client's account page.
//
// The shapes are pinned by the client's AccountGateway, which shipped first: camelCase JSON,
// errors as {"error","message"}, and exactly these seven routes. Changing any of them is a
// breaking change to a client that is already in the field, so they are documented in
// docs/CONTRACTS.md next to the proto rules.
type accountHandler struct {
	svc accountapi.Service
}

type profileResponse struct {
	DisplayName      string `json:"displayName"`
	Email            string `json:"email"`
	Phone            string `json:"phone,omitempty"`
	TwoFactorEnabled bool   `json:"twoFactorEnabled"`
}

type updateProfileRequest struct {
	DisplayName *string `json:"displayName"`
	Phone       *string `json:"phone"`
}

type changePasswordRequest struct {
	CurrentPassword string `json:"currentPassword"`
	NewPassword     string `json:"newPassword"`
}

type sessionResponse struct {
	ID         string    `json:"id"`
	Client     string    `json:"client"`
	Device     string    `json:"device,omitempty"`
	IPAddress  string    `json:"ipAddress,omitempty"`
	CreatedAt  time.Time `json:"createdAt"`
	LastSeenAt time.Time `json:"lastSeenAt"`
	IsCurrent  bool      `json:"isCurrent"`
}

type securityEventResponse struct {
	Kind       string    `json:"kind"`
	Device     string    `json:"device,omitempty"`
	IPAddress  string    `json:"ipAddress,omitempty"`
	OccurredAt time.Time `json:"occurredAt"`
}

type errorResponse struct {
	Error   string `json:"error"`
	Message string `json:"message"`
}

func (h *accountHandler) profile(w http.ResponseWriter, r *http.Request) {
	subject, ok := requireSubject(w, r)
	if !ok {
		return
	}

	account, err := h.svc.Profile(r.Context(), subject.ID)
	if err != nil {
		writeError(w, err)
		return
	}

	writeJSON(w, profileResponse{
		DisplayName:      account.DisplayName,
		Email:            account.Email,
		Phone:            account.Phone,
		TwoFactorEnabled: account.TwoFactorEnabled,
	})
}

func (h *accountHandler) updateProfile(w http.ResponseWriter, r *http.Request) {
	subject, ok := requireSubject(w, r)
	if !ok {
		return
	}

	var body updateProfileRequest
	if !readJSON(w, r, &body) {
		return
	}

	if err := h.svc.UpdateProfile(
		r.Context(), subject.ID, body.DisplayName, body.Phone); err != nil {
		writeError(w, err)
		return
	}
	w.WriteHeader(http.StatusNoContent)
}

func (h *accountHandler) changePassword(w http.ResponseWriter, r *http.Request) {
	subject, ok := requireSubject(w, r)
	if !ok {
		return
	}

	var body changePasswordRequest
	if !readJSON(w, r, &body) {
		return
	}

	if err := h.svc.ChangePassword(
		r.Context(), subject.ID, body.CurrentPassword, body.NewPassword); err != nil {
		writeError(w, err)
		return
	}
	w.WriteHeader(http.StatusNoContent)
}

func (h *accountHandler) sessions(w http.ResponseWriter, r *http.Request) {
	subject, ok := requireSubject(w, r)
	if !ok {
		return
	}

	sessions, err := h.svc.Sessions(r.Context(), subject.ID, subject.SessionID)
	if err != nil {
		writeError(w, err)
		return
	}

	// An empty array rather than null: the client deserializes into a list, and "no sessions" is
	// a list with nothing in it, not the absence of a list.
	out := make([]sessionResponse, 0, len(sessions))
	for _, s := range sessions {
		out = append(out, sessionResponse{
			ID:         s.ID,
			Client:     s.Client,
			Device:     s.Device,
			IPAddress:  s.IPAddress,
			CreatedAt:  s.CreatedAt,
			LastSeenAt: s.LastSeenAt,
			IsCurrent:  s.IsCurrent,
		})
	}
	writeJSON(w, out)
}

func (h *accountHandler) revokeSession(w http.ResponseWriter, r *http.Request) {
	subject, ok := requireSubject(w, r)
	if !ok {
		return
	}

	if err := h.svc.RevokeSession(r.Context(), subject.ID, r.PathValue("id")); err != nil {
		writeError(w, err)
		return
	}
	w.WriteHeader(http.StatusNoContent)
}

func (h *accountHandler) revokeAllSessions(w http.ResponseWriter, r *http.Request) {
	subject, ok := requireSubject(w, r)
	if !ok {
		return
	}

	if err := h.svc.RevokeAllSessions(r.Context(), subject.ID); err != nil {
		writeError(w, err)
		return
	}
	w.WriteHeader(http.StatusNoContent)
}

func (h *accountHandler) securityEvents(w http.ResponseWriter, r *http.Request) {
	subject, ok := requireSubject(w, r)
	if !ok {
		return
	}

	take, _ := strconv.Atoi(r.URL.Query().Get("take"))
	events, err := h.svc.SecurityEvents(r.Context(), subject.ID, take)
	if err != nil {
		writeError(w, err)
		return
	}

	out := make([]securityEventResponse, 0, len(events))
	for _, e := range events {
		out = append(out, securityEventResponse{
			Kind:       e.Kind,
			Device:     e.Device,
			IPAddress:  e.IPAddress,
			OccurredAt: e.OccurredAt,
		})
	}
	writeJSON(w, out)
}

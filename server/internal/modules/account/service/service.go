// Package service implements the account use cases. It is private to the module.
//
// The files here are grouped by the caller that drives them, not by the aggregate they touch,
// because most of these use cases touch several. signin.go holds the three calls the sign-in
// handlers make in sequence — the exact three that accountapi.Service withholds from other
// modules. profile.go, sessions.go and securityevent.go hold the seven behind the /account
// endpoints.
//
// This file is the seam: the port, the injected dependencies, the type and its constructor. No
// use case belongs here.
package service

import (
	"context"
	"time"

	accountapi "github.com/SekiroKenjii/kakehashi/server/internal/modules/account/api"
	"github.com/SekiroKenjii/kakehashi/server/internal/modules/account/domain"
	"github.com/SekiroKenjii/kakehashi/server/internal/platform/eventbus"
)

// Store is the persistence these use cases need, declared here so they can be tested against a
// fake. See the notes module for the longer argument; it applies unchanged.
type Store interface {
	AccountByID(ctx context.Context, id string) (domain.Account, error)
	AccountByEmail(ctx context.Context, email string) (domain.Account, error)
	Accounts(ctx context.Context) ([]domain.Account, error)
	InsertAccount(ctx context.Context, u domain.Account) error
	UpdateAccount(ctx context.Context, u domain.Account) error
	DeleteAccount(ctx context.Context, id string) error
	TouchSignIn(ctx context.Context, id string, at time.Time) error
	SetActive(ctx context.Context, id string, active bool) error

	InsertSession(ctx context.Context, sess domain.UserSession) error
	SessionCountsByAccount(ctx context.Context) (map[string]int, error)
	SessionsForUser(ctx context.Context, userID string) ([]domain.UserSession, error)
	DeleteSession(ctx context.Context, userID, id string) (bool, error)
	DeleteSessionsForUser(ctx context.Context, userID string) (int64, error)

	CompleteAuthRequest(ctx context.Context, id, subject, sessionID string, at time.Time) error

	InsertSecurityEvent(ctx context.Context, e domain.SecurityEvent) error
	SecurityEventsForUser(
		ctx context.Context, userID string, take int) ([]domain.SecurityEvent, error)
}

type (
	// Clock is the service's source of time, injected so a test can pin it. A service that reaches
	// for time.Now has to assert on shapes instead of values.
	Clock func() time.Time

	// IDs is the service's source of identifiers, injected for the same reason as Clock.
	IDs func() string
)

// Service implements accountapi.Service and the authentication both sign-in paths drive.
type Service struct {
	store Store
	bus   *eventbus.Bus
	now   Clock
	newID IDs
}

// New builds the service. Pass nil for clock or ids to use the wall clock and random UUIDs.
func New(store Store, bus *eventbus.Bus, clock Clock, ids IDs) *Service {
	if clock == nil {
		clock = time.Now
	}
	if ids == nil {
		ids = newUUID
	}
	return &Service{store: store, bus: bus, now: clock, newID: ids}
}

var _ accountapi.Service = (*Service)(nil)

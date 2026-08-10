// The fake store and the harness every test in this package builds on. No tests of its own —
// hence the name: a service_test.go with nothing to run in it reads as one somebody forgot to
// delete.

package service

import (
	"context"
	"fmt"
	"io"
	"log/slog"
	"testing"
	"time"

	"github.com/SekiroKenjii/kakehashi/server/internal/modules/account/domain"
	"github.com/SekiroKenjii/kakehashi/server/internal/platform/errs"
	"github.com/SekiroKenjii/kakehashi/server/internal/platform/eventbus"
)

const password = "correct horse battery staple"

var frozen = time.Date(2026, time.August, 5, 12, 0, 0, 0, time.UTC)

type fakeStore struct {
	accounts map[string]domain.Account
	sessions map[string]domain.UserSession
	events   []domain.SecurityEvent

	completed  []string
	updateErr  error
	sessionErr error
}

func newFakeStore() *fakeStore {
	return &fakeStore{
		accounts: map[string]domain.Account{},
		sessions: map[string]domain.UserSession{},
	}
}

func (f *fakeStore) seed(t *testing.T, email string) domain.Account {
	t.Helper()
	account, err := domain.NewAccount("account-1", email, "Ada Lovelace", password, frozen)
	if err != nil {
		t.Fatalf("NewAccount returned an error: %v", err)
	}
	f.accounts[account.ID] = account
	return account
}

func (f *fakeStore) AccountByID(_ context.Context, id string) (domain.Account, error) {
	account, ok := f.accounts[id]
	if !ok {
		return domain.Account{}, errs.NotFoundf("No account for id %s.", id)
	}
	return account, nil
}

func (f *fakeStore) AccountByEmail(_ context.Context, email string) (domain.Account, error) {
	for _, account := range f.accounts {
		if account.Email == email {
			return account, nil
		}
	}
	return domain.Account{}, errs.NotFoundf("No account for email %s.", email)
}

func (f *fakeStore) Accounts(_ context.Context) ([]domain.Account, error) {
	out := make([]domain.Account, 0, len(f.accounts))
	for _, u := range f.accounts {
		out = append(out, u)
	}
	return out, nil
}

func (f *fakeStore) TouchSignIn(_ context.Context, id string, at time.Time) error {
	u, ok := f.accounts[id]
	if !ok {
		return errs.NotFoundf("No account with ID %s.", id)
	}
	u.LastSignInAt = at
	f.accounts[id] = u
	return nil
}

func (f *fakeStore) SetActive(_ context.Context, id string, active bool) error {
	u, ok := f.accounts[id]
	if !ok {
		return errs.NotFoundf("No account with ID %s.", id)
	}
	u.IsActive = active
	f.accounts[id] = u
	return nil
}

func (f *fakeStore) InsertAccount(_ context.Context, u domain.Account) error {
	for _, existing := range f.accounts {
		if existing.Email == u.Email {
			return errs.Conflictf("An account with that email address already exists.")
		}
	}
	f.accounts[u.ID] = u
	return nil
}

func (f *fakeStore) DeleteAccount(_ context.Context, id string) error {
	if _, ok := f.accounts[id]; !ok {
		return errs.NotFoundf("No account with ID %s.", id)
	}
	delete(f.accounts, id)
	for sid, sess := range f.sessions {
		if sess.UserID == id {
			delete(f.sessions, sid)
		}
	}
	return nil
}

func (f *fakeStore) UpdateAccount(_ context.Context, u domain.Account) error {
	if f.updateErr != nil {
		return f.updateErr
	}
	f.accounts[u.ID] = u
	return nil
}

func (f *fakeStore) InsertSession(_ context.Context, sess domain.UserSession) error {
	if f.sessionErr != nil {
		return f.sessionErr
	}
	f.sessions[sess.ID] = sess
	return nil
}

func (f *fakeStore) SessionCountsByAccount(_ context.Context) (map[string]int, error) {
	out := map[string]int{}
	for _, sess := range f.sessions {
		out[sess.UserID]++
	}
	return out, nil
}

func (f *fakeStore) SessionsForUser(_ context.Context, userID string) ([]domain.UserSession, error) {
	var out []domain.UserSession
	for _, sess := range f.sessions {
		if sess.UserID == userID {
			out = append(out, sess)
		}
	}
	return out, nil
}

func (f *fakeStore) DeleteSession(_ context.Context, userID, id string) error {
	if sess, ok := f.sessions[id]; ok && sess.UserID == userID {
		delete(f.sessions, id)
	}
	return nil
}

func (f *fakeStore) DeleteSessionsForUser(_ context.Context, userID string) error {
	for id, sess := range f.sessions {
		if sess.UserID == userID {
			delete(f.sessions, id)
		}
	}
	return nil
}

func (f *fakeStore) CompleteAuthRequest(
	_ context.Context, id, subject, sessionID string, _ time.Time,
) error {
	f.completed = append(f.completed, fmt.Sprintf("%s:%s:%s", id, subject, sessionID))
	return nil
}

func (f *fakeStore) InsertSecurityEvent(_ context.Context, e domain.SecurityEvent) error {
	f.events = append(f.events, e)
	return nil
}

func (f *fakeStore) SecurityEventsForUser(
	_ context.Context, userID string, take int,
) ([]domain.SecurityEvent, error) {
	var out []domain.SecurityEvent
	for _, e := range f.events {
		if e.UserID == userID && len(out) < take {
			out = append(out, e)
		}
	}
	return out, nil
}

func (f *fakeStore) kinds() []string {
	out := make([]string, len(f.events))
	for i, e := range f.events {
		out[i] = e.Kind
	}
	return out
}

func newService(store *fakeStore) *Service {
	bus := eventbus.New(slog.New(slog.NewTextHandler(io.Discard, nil)))
	sequence := 0
	return New(store, bus,
		func() time.Time { return frozen },
		func() string { sequence++; return fmt.Sprintf("id-%d", sequence) })
}

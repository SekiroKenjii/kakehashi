// Package service implements the activity use cases: append a fact, read the feed back. It is
// private to the module and imports no other module; its whole foreign vocabulary is its own
// domain type, which keeps its tests free of any other module's events.
package service

import (
	"context"
	"encoding/base64"
	"strings"
	"time"

	"github.com/google/uuid"

	activityapi "__GO_MODULE__/server/internal/modules/activity/api"
	"__GO_MODULE__/server/internal/modules/activity/domain"
	"__GO_MODULE__/server/internal/platform/errs"
)

// Store is the persistence these use cases need, declared here rather than in store/: the
// interface belongs to the consumer, so the use cases test against a fake, and a package that
// must never import another module names nothing but its own domain. The longer argument is at
// the notes module's Store.
type Store interface {
	Insert(ctx context.Context, e domain.Entry) error
	List(
		ctx context.Context, userID string, filter domain.Filter, take int,
	) ([]domain.Entry, error)
	Count(ctx context.Context, userID string, filter domain.Filter) (int, error)
	CountByKind(ctx context.Context, userID string, filter domain.Filter) (map[string]int, error)
}

// IDs mints an entry identifier. Injected so tests can pin it instead of asserting on shapes.
type IDs func() string

// Service implements activityapi.Service, plus the Record that no other module may reach.
type Service struct {
	store Store
	newID IDs
}

// New builds the service. Pass nil for ids to use random UUIDs.
func New(store Store, ids IDs) *Service {
	if ids == nil {
		ids = uuid.NewString
	}
	return &Service{store: store, newID: ids}
}

// Record appends one fact to an account's feed.
//
// It is absent from activityapi.Service on purpose: a feed another module can write into is a feed
// every module must call, which is the inverted dependency this module exists to avoid.
//
// `at` is when the fact happened, not when the row is written. The bus delivers synchronously, so
// the two coincide; naming the parameter for the fact keeps the feed correct the first time a
// handler defers its work.
func (s *Service) Record(
	ctx context.Context, userID, kind, sessionID, device, ip string, at time.Time,
) error {
	// Ask the domain, then the store. The service orchestrates; it does not re-implement the rules.
	entry, err := domain.NewEntry(s.newID(), userID, kind, sessionID, device, ip, at)
	if err != nil {
		return err
	}
	return s.store.Insert(ctx, entry)
}

// Feed page sizes.
const (
	defaultPageSize = 50
	maxPageSize     = 200
)

// List returns one page of the account's feed, newest first.
func (s *Service) List(
	ctx context.Context, userID string, q activityapi.Query,
) (activityapi.Page, error) {
	take := clamp(q.PageSize)

	after, err := decodeCursor(q.PageToken)
	if err != nil {
		return activityapi.Page{}, err
	}

	filter := domain.Filter{
		From:  q.From,
		To:    q.To,
		Kinds: activityapi.KindsIn(q.Category),
		Query: q.Search,
		After: after,
	}

	// One more than asked for, so this read observes whether a next page exists: a page that comes
	// back exactly full is otherwise indistinguishable from the last one.
	entries, err := s.store.List(ctx, userID, filter, take+1)
	if err != nil {
		return activityapi.Page{}, err
	}

	var next string
	if len(entries) > take {
		last := entries[take-1]
		next = encodeCursor(domain.Cursor{OccurredAt: last.OccurredAt, ID: last.ID})
		entries = entries[:take]
	}

	// A total counts what matches, not what is left below the page somebody is on, so the cursor
	// comes off. Decided here rather than inside the store, which counts exactly what it is given.
	totals := filter
	totals.After = nil

	total, err := s.store.Count(ctx, userID, totals)
	if err != nil {
		return activityapi.Page{}, err
	}

	page := activityapi.Page{
		Entries:       make([]activityapi.Entry, len(entries)),
		NextPageToken: next,
		Total:         total,
		RetentionDays: int(domain.Retention.Hours() / 24),
	}
	for i, e := range entries {
		page.Entries[i] = toAPI(e)
	}

	// Only on the first page. Paging cannot change them, the client already has them, and each set
	// costs an aggregation.
	if q.PageToken == "" {
		// One aggregation feeds both. The per-kind numbers are what the store answers with anyway, so
		// the category totals are a fold over them rather than a second query.
		byKind, err := s.store.CountByKind(ctx, userID, withoutCategory(totals))
		if err != nil {
			return activityapi.Page{}, err
		}

		byCategory := make(map[string]int, len(byKind))
		for kind, count := range byKind {
			byCategory[activityapi.CategoryOf(kind)] += count
		}
		page.KindCounts = byKind
		page.Counts = byCategory
	}
	return page, nil
}

// withoutCategory drops the chip from a filter.
//
// The counts are what every chip shows while one of them is active, so they are taken over the
// whole set rather than over the filtered view — otherwise every chip but the active one reads
// zero. The range and the search still apply: those are what the reader chose to look at.
func withoutCategory(filter domain.Filter) domain.Filter {
	filter.Kinds = nil
	return filter
}

// clamp keeps a page size sane.
//
// A true ceiling rather than a reset to the default: asking for 500 and silently getting 50 would
// make a client's paging arithmetic wrong in a way nothing reports. A non-positive size still
// means the default, because zero is what an unset field looks like on the wire.
func clamp(size int) int {
	switch {
	case size <= 0:
		return defaultPageSize
	case size > maxPageSize:
		return maxPageSize
	default:
		return size
	}
}

// encodeCursor makes a page position opaque.
//
// Opaque on purpose: the moment a client can read a token it can also compose one, and then the
// shape of the cursor is contract and can never change. Base64 of two fields is not encryption and
// is not meant to be — it is a "do not depend on this" sign that a client cannot miss.
func encodeCursor(c domain.Cursor) string {
	return base64.RawURLEncoding.EncodeToString(
		[]byte(c.OccurredAt.UTC().Format(time.RFC3339Nano) + "|" + c.ID))
}

// decodeCursor reads one back, refusing anything it did not write.
//
// Refused rather than ignored. A token this server cannot read means the client and the server
// disagree about where the caller is, and quietly restarting from the newest entry would show the
// first page again under a "Load more" button — the reader would conclude the feed loops.
func decodeCursor(token string) (*domain.Cursor, error) {
	if token == "" {
		return nil, nil
	}

	const message = "That page of your activity could not be read. Reload the list."

	raw, err := base64.RawURLEncoding.DecodeString(token)
	if err != nil {
		return nil, errs.Invalidf(message)
	}
	at, id, ok := strings.Cut(string(raw), "|")
	if !ok || id == "" {
		return nil, errs.Invalidf(message)
	}
	occurredAt, err := time.Parse(time.RFC3339Nano, at)
	if err != nil {
		return nil, errs.Invalidf(message)
	}
	return &domain.Cursor{OccurredAt: occurredAt, ID: id}, nil
}

// toAPI maps a domain entry to the api type; nothing leaves the module except through here.
//
// The entry id goes out: the reader of a row is the account that owns it, and a screen offering
// "copy this event" needs something to copy. The user id stops here — it is the query's filter,
// never its result.
//
// Category and Platform are computed on the way out rather than stored, so a better answer
// applies to every row already written instead of only to the ones recorded after the change.
func toAPI(e domain.Entry) activityapi.Entry {
	return activityapi.Entry{
		ID:         e.ID,
		Kind:       e.Kind,
		Category:   activityapi.CategoryOf(e.Kind),
		SessionID:  e.SessionID,
		Device:     e.Device,
		Platform:   domain.PlatformOf(e.Device),
		IPAddress:  e.IPAddress,
		OccurredAt: e.OccurredAt,
	}
}

var _ activityapi.Service = (*Service)(nil)

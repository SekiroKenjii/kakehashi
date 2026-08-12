// Package store persists activity entries in MongoDB.
//
// It is the only package in the module permitted to import platform/mongodb, and archlint enforces
// that. Mongo has no migrations, only indexes, so what this package declares through Indexes() is
// the whole of its schema management.
//
// There is deliberately no `storable` truncation helper, unlike notes/store: the driver truncates
// to milliseconds on encode and decodes back as UTC, and nothing here observes it, because the
// subscriber discards the entry after writing. The day an Insert returns the stored entry to a
// caller that compares it, the helper comes back.
package store

import (
	"context"
	"regexp"
	"time"

	"go.mongodb.org/mongo-driver/v2/bson"
	"go.mongodb.org/mongo-driver/v2/mongo"
	"go.mongodb.org/mongo-driver/v2/mongo/options"

	"github.com/SekiroKenjii/kakehashi/server/internal/modules/activity/domain"
	"github.com/SekiroKenjii/kakehashi/server/internal/platform/errs"
	"github.com/SekiroKenjii/kakehashi/server/internal/platform/mongodb"
)

// collection must start with the module's ID and an underscore; EnsureIndexes fails the boot
// otherwise, which is the one namespacing rule in this project a machine can actually check.
const collection = "activity_entry"

func Indexes() []mongodb.Index {
	return []mongodb.Index{
		{
			// Named after its keys rather than its query. EnsureIndexes creates by name with
			// CreateOne and Mongo cannot alter an existing index, so a name kept while its keys
			// change fails the boot of every database that already has the old one, with no
			// migration to fix it. Change the name with the keys.
			Name:       "IX_Entry_UserId_OccurredAt_Id",
			Collection: collection,
			Keys: []mongodb.Key{
				// The equality field leads, so one traversal satisfies both the match and the sort
				// and the sort is never done in memory. An index led by occurred_at would serve
				// "everyone's activity, newest first" — a query this module must never answer,
				// having no basis on which to authorize a cross-account read.
				{Field: "user_id"},
				{Field: "occurred_at", Descending: true},

				// The tiebreaker. BSON dates are millisecond-precision, so same-millisecond
				// collisions are real, and a list that reshuffles between refreshes looks broken
				// even when the data is right. It is also exactly the key a keyset cursor needs.
				{Field: "_id", Descending: true},
			},
		},
		{
			// Retention, and the only thing in this module that removes anything. Its own index
			// because Mongo honours a TTL only over a single date field, so it cannot be a flag on
			// the compound index above.
			//
			// Deleting is Mongo's job rather than one this server schedules: a background sweep
			// would be a second thing to deploy, to monitor and to keep from running on every
			// replica at once. The trade is that expiry is approximate — the TTL monitor wakes
			// about once a minute — which is the precision "kept for ninety days" deserves.
			Name:        "IX_Entry_OccurredAt_TTL",
			Collection:  collection,
			Keys:        []mongodb.Key{{Field: "occurred_at"}},
			ExpireAfter: domain.Retention,
		},
	}
	// `kind` is deliberately not indexed. CountByKind groups by it, but only after a match on
	// user_id and a date range the compound index above serves completely, so the group runs over
	// one account's window rather than the collection. An index on `kind` would earn its write cost
	// only if something matched on `kind` first, and nothing does: every read here starts from
	// "whose feed is this".
}

type Mongo struct {
	entries *mongo.Collection
}

func New(db *mongodb.DB) *Mongo {
	return &Mongo{entries: db.Collection(collection)}
}

// There is no update and no delete: the feed is append-only, and the absence of those methods is
// what says so.
func (s *Mongo) Insert(ctx context.Context, e domain.Entry) error {
	if _, err := s.entries.InsertOne(ctx, toDocument(e)); err != nil {
		return errs.Internalf(err, "insert activity entry")
	}
	return nil
}

func (s *Mongo) List(
	ctx context.Context, userID string, filter domain.Filter, take int,
) ([]domain.Entry, error) {
	find := options.Find().
		SetSort(bson.D{{Key: "occurred_at", Value: -1}, {Key: "_id", Value: -1}}).
		SetLimit(int64(take))

	cursor, err := s.entries.Find(ctx, queryFor(userID, filter), find)
	if err != nil {
		return nil, errs.Internalf(err, "list activity entries")
	}

	var documents []document
	if err := cursor.All(ctx, &documents); err != nil {
		return nil, errs.Internalf(err, "list activity entries")
	}

	out := make([]domain.Entry, len(documents))
	for i, d := range documents {
		out[i] = toDomain(d)
	}
	return out, nil
}

// Counts exactly the filter it is given, cursor included. Whether a total should ignore the page
// somebody is on is the caller's decision, and a store method that quietly dropped a field it was
// handed would surprise the next caller who needed that field honoured.
func (s *Mongo) Count(
	ctx context.Context, userID string, filter domain.Filter,
) (int, error) {
	total, err := s.entries.CountDocuments(ctx, queryFor(userID, filter))
	if err != nil {
		return 0, errs.Internalf(err, "count activity entries")
	}
	return int(total), nil
}

// Keyed by kind rather than by category for the reason Filter.Kinds is: the grouping belongs to the
// api package, and the caller folds these into it.
func (s *Mongo) CountByKind(
	ctx context.Context, userID string, filter domain.Filter,
) (map[string]int, error) {
	pipeline := []bson.D{
		{{Key: "$match", Value: queryFor(userID, filter)}},
		{{Key: "$group", Value: bson.D{
			{Key: "_id", Value: "$kind"},
			{Key: "count", Value: bson.D{{Key: "$sum", Value: 1}}},
		}}},
	}

	cursor, err := s.entries.Aggregate(ctx, pipeline)
	if err != nil {
		return nil, errs.Internalf(err, "count activity entries by kind")
	}

	var groups []struct {
		Kind  string `bson:"_id"`
		Count int    `bson:"count"`
	}
	if err := cursor.All(ctx, &groups); err != nil {
		return nil, errs.Internalf(err, "count activity entries by kind")
	}

	out := make(map[string]int, len(groups))
	for _, g := range groups {
		out[g.Kind] = g.Count
	}
	return out, nil
}

// Clauses go into an explicit $and rather than one flat document, because a range and a keyset
// cursor both constrain occurred_at, and a BSON document with the same key twice is not a
// document — one of the two silently wins.
func queryFor(userID string, f domain.Filter) bson.D {
	clauses := []bson.D{{{Key: "user_id", Value: userID}}}

	if !f.From.IsZero() {
		clauses = append(clauses, bson.D{
			{Key: "occurred_at", Value: bson.D{{Key: "$gte", Value: f.From}}},
		})
	}
	if !f.To.IsZero() {
		clauses = append(clauses, bson.D{
			{Key: "occurred_at", Value: bson.D{{Key: "$lte", Value: f.To}}},
		})
	}
	if len(f.Kinds) > 0 {
		clauses = append(clauses, bson.D{
			{Key: "kind", Value: bson.D{{Key: "$in", Value: f.Kinds}}},
		})
	}
	if f.Query != "" {
		// Escaped, because the caller's text reaches a regular-expression engine: an unescaped "("
		// is a syntax error the driver reports as a failed read, and an unescaped ".*" is a scan
		// nobody asked for.
		//
		// A regex rather than a text index, which would tokenise on word boundaries: the three
		// fields covered are an event kind, a user-agent string and an IP address — none of them
		// prose, and all searched by fragment ("203.0", "iPhone"). It runs over one account's
		// window, after the index has done the narrowing.
		needle := bson.Regex{Pattern: regexp.QuoteMeta(f.Query), Options: "i"}
		clauses = append(clauses, bson.D{{Key: "$or", Value: []bson.D{
			{{Key: "kind", Value: needle}},
			{{Key: "device", Value: needle}},
			{{Key: "ip_address", Value: needle}},
		}}})
	}
	if f.After != nil {
		// Keyset, matching the sort and the index exactly: strictly older, or the same instant with
		// a smaller id. Never skip-and-limit — new rows land at the head of this collection between
		// any two reads, which is what offset paging gets wrong.
		clauses = append(clauses, bson.D{{Key: "$or", Value: []bson.D{
			{{Key: "occurred_at", Value: bson.D{{Key: "$lt", Value: f.After.OccurredAt}}}},
			{
				{Key: "occurred_at", Value: f.After.OccurredAt},
				{Key: "_id", Value: bson.D{{Key: "$lt", Value: f.After.ID}}},
			},
		}}})
	}

	if len(clauses) == 1 {
		return clauses[0]
	}
	return bson.D{{Key: "$and", Value: clauses}}
}

// The bson tags live here rather than on domain.Entry, where archlint would never object to
// them — a struct tag imports nothing. Mongo field names are load-bearing forever in a store with
// no migrations, while domain.Entry's are refactorable at will; tying them together turns a Go
// rename into a data migration.
//
// The id is a UUID string rather than a bson.ObjectID for the same class of reason: ObjectID is a
// driver type, and putting one on domain.Entry would be a Mongo type in the innermost layer. Rule 5
// fences platform/mongodb and would not catch it, so this is a line a review holds rather than a
// linter.
type document struct {
	ID     string `bson:"_id"`
	UserID string `bson:"user_id"`
	Kind   string `bson:"kind"`

	// Added after rows already existed, and needing no backfill: a document written before this
	// field decodes it as empty, which is exactly what "this fact had no session" means.
	SessionID string `bson:"session_id"`

	Device     string    `bson:"device"`
	IPAddress  string    `bson:"ip_address"`
	OccurredAt time.Time `bson:"occurred_at"`
}

func toDocument(e domain.Entry) document {
	return document{
		ID:         e.ID,
		UserID:     e.UserID,
		Kind:       e.Kind,
		SessionID:  e.SessionID,
		Device:     e.Device,
		IPAddress:  e.IPAddress,
		OccurredAt: e.OccurredAt,
	}
}

func toDomain(d document) domain.Entry {
	return domain.Entry{
		ID:         d.ID,
		UserID:     d.UserID,
		Kind:       d.Kind,
		SessionID:  d.SessionID,
		Device:     d.Device,
		IPAddress:  d.IPAddress,
		OccurredAt: d.OccurredAt,
	}
}

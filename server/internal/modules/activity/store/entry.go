// Package store persists activity entries in MongoDB.
//
// It is the only package in the module permitted to import platform/mongodb, and archlint enforces
// that. Mongo has no migrations, only indexes, so what this package declares through Indexes() is
// the whole of its schema management.
//
// One file, because there is one collection. The decomposition rule makes store/'s unit the table
// or collection; an axis with one value has nothing to split, and a store.go beside an entry.go
// would be a rename rather than a seam. The name is entry.go and not mongo.go for the reason
// CLAUDE.md gives: a file named after its technology is a file whose contents refused a name.
//
// For a reader arriving from notes/store: there is deliberately no `storable` truncation helper
// here. The driver truncates to milliseconds on encode and decodes back as UTC, and nothing in
// this module observes it, because the subscriber discards the entry after writing. The day an
// Insert returns the stored entry to a caller that compares it, the helper comes back.
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

// Indexes is the module's entire schema management.
func Indexes() []mongodb.Index {
	return []mongodb.Index{
		{
			// Named after its keys rather than its query, on purpose. EnsureIndexes creates by
			// name with CreateOne and Mongo cannot alter an existing index, so a name kept while
			// its keys change fails the boot of every database that already holds that name — and
			// there is no migration to fix it with. Change the name with the keys.
			Name:       "IX_Entry_UserId_OccurredAt_Id",
			Collection: collection,
			Keys: []mongodb.Key{
				// The equality field leads, so one traversal satisfies both the match and the sort
				// and the sort is never done in memory. An index led by occurred_at would serve
				// "everyone's activity, newest first" — a query this module must never answer,
				// because it has no basis on which to authorize a cross-account read.
				{Field: "user_id"},
				{Field: "occurred_at", Descending: true},

				// The tiebreaker. BSON dates are millisecond-precision, so same-millisecond
				// collisions are real, and a list that reshuffles between refreshes looks broken
				// even when the data is right. It is also exactly the key a keyset cursor needs,
				// and it is not retrofittable — an index is rebuilt, but the ordering guarantee
				// people already relied on is not.
				{Field: "_id", Descending: true},
			},
		},
		{
			// Retention, and the only thing in this module that removes anything. Its own index
			// because Mongo honours a TTL only over a single date field, so it cannot be a flag on
			// the compound index above.
			//
			// Deleting is Mongo's job rather than a job this server schedules: a background sweep
			// here would be a second thing to deploy, to monitor and to get wrong, and it would have
			// to be careful not to run on every replica at once. The trade is that expiry is
			// approximate — the TTL monitor wakes about once a minute — which is exactly the
			// precision "kept for ninety days" deserves.
			Name:        "IX_Entry_OccurredAt_TTL",
			Collection:  collection,
			Keys:        []mongodb.Key{{Field: "occurred_at"}},
			ExpireAfter: domain.Retention,
		},
	}
	// `kind` is still not indexed, and the counts are why that is now a decision rather than an
	// oversight. CountByKind groups by it, but only after a match on user_id and a date range that
	// the compound index above serves completely — so the group runs over one account's window,
	// which is small, rather than over the collection. An index on `kind` would earn its write cost
	// only if something matched on `kind` first, and nothing does: every read here starts from
	// "whose feed is this".
}

// Mongo is the activity module's document store.
type Mongo struct {
	entries *mongo.Collection
}

// New binds the store to its collection.
func New(db *mongodb.DB) *Mongo {
	return &Mongo{entries: db.Collection(collection)}
}

// Insert appends one entry. There is no update and no delete: the feed is append-only, and the
// absence of those methods is what says so.
func (s *Mongo) Insert(ctx context.Context, e domain.Entry) error {
	if _, err := s.entries.InsertOne(ctx, toDocument(e)); err != nil {
		return errs.Internalf(err, "insert activity entry")
	}
	return nil
}

// List returns one account's entries matching the filter, newest first.
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

// Count is how many entries match, which is the "of 214" a footer reports.
//
// It counts exactly the filter it is given, cursor included. Whether a total should ignore the page
// somebody is on is the caller's decision, and a store method that quietly dropped a field it was
// handed would be a surprise for the next caller who needed that field honoured.
func (s *Mongo) Count(
	ctx context.Context, userID string, filter domain.Filter,
) (int, error) {
	total, err := s.entries.CountDocuments(ctx, queryFor(userID, filter))
	if err != nil {
		return 0, errs.Internalf(err, "count activity entries")
	}
	return int(total), nil
}

// CountByKind is how many entries there are of each kind, for the counts beside a chip.
//
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

// query builds the match. Clauses go into an explicit $and rather than one flat document, because a
// range and a keyset cursor both constrain occurred_at and a BSON document with the same key twice
// is not a document — one of the two silently wins.
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
		// somebody did not mean to ask for.
		//
		// A regex rather than a text index. A text index would tokenise on word boundaries, and the
		// three fields it would cover are an event kind, a user-agent string and an IP address —
		// none of which is prose, and all of which people search by fragment ("203.0", "iPhone").
		// It runs over one account's window, after the index has done the narrowing.
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
		// any two reads, which is the case offset paging gets wrong.
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

// document is the stored shape.
//
// The bson tags live here rather than on domain.Entry, and the reason is that archlint would never
// object if they did — a struct tag imports nothing. Mongo field names are load-bearing forever,
// baked into every stored document and every index in a store with no migrations, while
// domain.Entry's field names are refactorable at will. Tying them together turns a Go rename into
// a data migration.
//
// The id is a UUID string rather than a bson.ObjectID for the same class of reason: ObjectID is a
// driver type, and putting one on domain.Entry would be a Mongo type in the innermost layer.
// Rule 5 fences platform/mongodb and would not catch it, so this is a line a review holds rather
// than a linter. Every other identifier in this server is a v4 UUID string.
type document struct {
	ID     string `bson:"_id"`
	UserID string `bson:"user_id"`
	Kind   string `bson:"kind"`

	// Added after rows already existed, and needing no backfill: a document written before this
	// field decodes it as empty, which is exactly what "this fact had no session" means. That is
	// the whole of schema evolution in a store whose absent fields have a truthful zero value.
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

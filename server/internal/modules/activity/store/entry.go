// Package store persists activity entries in MongoDB. It is the only package in the module
// permitted to import platform/mongodb, and archlint enforces that. Mongo has no migrations, only
// indexes, so what this package declares through Indexes() is the whole of its schema management;
// the storage shape is documented in docs/ACTIVITY.md.
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
// otherwise.
const collection = "activity_entry"

// Indexes is the module's entire schema management.
func Indexes() []mongodb.Index {
	return []mongodb.Index{
		{
			// Change this name whenever the keys change. Mongo cannot alter an existing index, so
			// a kept name with new keys fails the boot of every database that already holds it.
			Name:       "IX_Entry_UserId_OccurredAt_Id",
			Collection: collection,
			Keys: []mongodb.Key{
				// The equality field leads, so one traversal serves both match and sort. Leading
				// with occurred_at would serve "everyone's activity" — never a query here.
				{Field: "user_id"},
				{Field: "occurred_at", Descending: true},

				// The tiebreaker the keyset cursor needs. BSON dates are millisecond-precision, so
				// without it the order between equal timestamps is unstable across reads.
				{Field: "_id", Descending: true},
			},
		},
		{
			// The only thing here that removes anything. Its own index because Mongo honours a TTL
			// only over a single date field. Why Mongo rather than a sweep: docs/ACTIVITY.md.
			Name:        "IX_Entry_OccurredAt_TTL",
			Collection:  collection,
			Keys:        []mongodb.Key{{Field: "occurred_at"}},
			ExpireAfter: domain.Retention,
		},
	}
	// `kind` is deliberately not indexed: every read matches user_id and a date range the compound
	// index already serves, so CountByKind groups over one account's window, not the collection.
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

// Count is how many entries match, which is the "of 214" a footer reports. It counts exactly the
// filter it is given, cursor included; whether a total should ignore the page somebody is on is
// the caller's decision.
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

// queryFor builds the match. Clauses go into an explicit $and rather than one flat document,
// because a range and a keyset cursor both constrain occurred_at, and in a BSON document with the
// same key twice one of the two silently wins.
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
		// Escaped: the text reaches a regex engine, where "(" is a failed read and ".*" a scan.
		// Regex not a text index: these fields are searched by fragment, not by word.
		needle := bson.Regex{Pattern: regexp.QuoteMeta(f.Query), Options: "i"}
		clauses = append(clauses, bson.D{{Key: "$or", Value: []bson.D{
			{{Key: "kind", Value: needle}},
			{{Key: "device", Value: needle}},
			{{Key: "ip_address", Value: needle}},
		}}})
	}
	if f.After != nil {
		// Keyset, matching the sort and index exactly. Never skip-and-limit: new rows land at the
		// head between any two reads, which is the case offset paging gets wrong.
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
// The bson tags live here rather than on domain.Entry: Mongo field names are load-bearing forever
// — baked into every stored document and every index in a store with no migrations — while
// domain.Entry's field names are refactorable at will. Tying them together turns a Go rename into
// a data migration, and archlint cannot catch it because a struct tag imports nothing.
//
// The id is a v4 UUID string rather than a bson.ObjectID: ObjectID is a driver type, and putting
// one on domain.Entry would be a Mongo type in the innermost layer. Review holds this line, not a
// linter.
type document struct {
	ID     string `bson:"_id"`
	UserID string `bson:"user_id"`
	Kind   string `bson:"kind"`

	// A document stored without this field decodes it as empty, which is what "this fact had no
	// session" means: absent fields with truthful zero values need no backfill.
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

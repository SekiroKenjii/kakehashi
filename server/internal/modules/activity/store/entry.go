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
			// its keys change fails the boot of every database that already has the old one — and
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
	}
	// `kind` is deliberately not indexed. Nothing filters or groups by it, and an index on a field
	// no query mentions is a write cost with no reader. It is the field people index reflexively;
	// make the next person justify it with a query.
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

// List returns one account's most recent entries, newest first.
func (s *Mongo) List(ctx context.Context, userID string, take int) ([]domain.Entry, error) {
	find := options.Find().
		SetSort(bson.D{{Key: "occurred_at", Value: -1}, {Key: "_id", Value: -1}}).
		SetLimit(int64(take))

	cursor, err := s.entries.Find(ctx, bson.D{{Key: "user_id", Value: userID}}, find)
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
	ID         string    `bson:"_id"`
	UserID     string    `bson:"user_id"`
	Kind       string    `bson:"kind"`
	Device     string    `bson:"device"`
	IPAddress  string    `bson:"ip_address"`
	OccurredAt time.Time `bson:"occurred_at"`
}

func toDocument(e domain.Entry) document {
	return document{
		ID:         e.ID,
		UserID:     e.UserID,
		Kind:       e.Kind,
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
		Device:     d.Device,
		IPAddress:  d.IPAddress,
		OccurredAt: d.OccurredAt,
	}
}

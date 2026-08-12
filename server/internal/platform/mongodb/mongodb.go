// Package mongodb wraps the server's document store.
//
// It is for what SQL Server is bad at: append-only streams that are read newest-first and never
// updated. Anything with invariants worth defending — anything where two writes must not both
// succeed — belongs in SQL Server, where a transaction and a unique constraint can say so.
//
// There are no migrations, because there is no schema to migrate. There are indexes, and a
// collection without one degrades from "fast" to "reads every document" quietly, at whatever size
// that starts to matter, so modules declare them the way they declare migrations and the kernel
// applies them at boot.
package mongodb

import (
	"context"
	"fmt"
	"strings"
	"time"

	"go.mongodb.org/mongo-driver/v2/bson"
	"go.mongodb.org/mongo-driver/v2/mongo"
	"go.mongodb.org/mongo-driver/v2/mongo/options"
)

type DB struct {
	client   *mongo.Client
	database *mongo.Database
}

type Options struct {
	URI      string
	Database string
}

// Index is spelled without any driver type so that a module's module.go can declare its indexes
// without importing this package, which tools/archlint reserves for store/ packages.
type Index struct {
	// Collection must start with the owning module's ID and an underscore.
	Collection string

	// Always set Name: an unnamed index gets a generated name derived from its keys, which makes
	// it awkward to talk about and awkward to drop.
	Name string

	// Unique is usually wrong on an append-only feed, and the whole point on an idempotency key.
	Unique bool

	// Keys are the indexed fields, in order. Order matters: an index on (a, b) serves a query on
	// a, and a query on a and b, but not a query on b alone.
	Keys []Key

	// ExpireAfter turns this into a TTL index: Mongo deletes a document once the indexed date is
	// that far in the past. Zero means it is an ordinary index.
	//
	// Two rules the driver will not enforce. Mongo only honours it on an index over a single date
	// field, so a TTL index cannot be the compound one a read is served by — it is always its own
	// index. And zero has to mean "not a TTL index" here, because the value the driver understands
	// as zero means "delete as soon as the date passes", which would empty a collection the moment
	// somebody left the field unset.
	ExpireAfter time.Duration
}

type Key struct {
	// Field is the document field name as stored: the bson tag, not the Go field name.
	Field string

	// Set Descending for the timestamp of a feed read newest-first: an ascending index can serve
	// that query, but only by walking it backwards.
	Descending bool
}

func (ix Index) model() mongo.IndexModel {
	keys := make(bson.D, 0, len(ix.Keys))
	for _, k := range ix.Keys {
		order := 1
		if k.Descending {
			order = -1
		}
		keys = append(keys, bson.E{Key: k.Field, Value: order})
	}

	opts := options.Index().SetName(ix.Name).SetUnique(ix.Unique)
	if ix.ExpireAfter > 0 {
		// SetExpireAfterSeconds(0) is a valid instruction meaning "expire the moment the date
		// passes", so an unset duration must not reach it.
		opts = opts.SetExpireAfterSeconds(int32(ix.ExpireAfter.Seconds()))
	}

	return mongo.IndexModel{Keys: keys, Options: opts}
}

func Open(ctx context.Context, opts Options) (*DB, error) {
	client, err := mongo.Connect(options.Client().
		ApplyURI(opts.URI).
		// Without a ceiling the driver blocks for its 30-second default while it picks a server.
		// An event handler runs synchronously on the publisher's goroutine, so a stalled Mongo
		// lands that wait on whatever request announced the fact — a sign-in, for the activity
		// module. Five seconds rather than two so a legitimate replica-set failover is not mistaken
		// for an outage.
		SetServerSelectionTimeout(5 * time.Second))
	if err != nil {
		return nil, fmt.Errorf("connect to mongo: %w", err)
	}

	// Connect is lazy, so without this ping the first sign of a bad URI or an unreachable host is
	// a failed query somewhere in a request, long after boot reported success.
	if err := client.Ping(ctx, nil); err != nil {
		_ = client.Disconnect(ctx)
		return nil, fmt.Errorf("ping mongo: %w", err)
	}

	return &DB{client: client, database: client.Database(opts.Database)}, nil
}

func (db *DB) Collection(name string) *mongo.Collection {
	return db.database.Collection(name)
}

// EnsureIndexes refuses any collection that is not the module's own. This is the one namespacing
// rule in the project a machine can enforce: the SQL Server side has the same convention and no way
// to check it, because table names are buried in SQL strings that only the database ever parses.
// Here every collection name passes through this function, so a typo or a reach into someone else's
// data fails the boot instead of the review.
func (db *DB) EnsureIndexes(ctx context.Context, module string, indexes []Index) error {
	prefix := module + "_"

	for _, ix := range indexes {
		if !strings.HasPrefix(ix.Collection, prefix) {
			return fmt.Errorf(
				"module %q may only index collections prefixed %q, got %q",
				module, prefix, ix.Collection)
		}

		if _, err := db.database.Collection(ix.Collection).
			Indexes().CreateOne(ctx, ix.model()); err != nil {
			return fmt.Errorf("create index %s on %s: %w", ix.Name, ix.Collection, err)
		}
	}

	return nil
}

func (db *DB) Close(ctx context.Context) error {
	return db.client.Disconnect(ctx)
}

// Package mongodb wraps the server's document store, used for append-only streams read
// newest-first and never updated (activity feeds, audit trails); anything where two writes must
// not both succeed belongs in SQL Server. There are no migrations, but an unindexed collection
// quietly degrades to reading every document, so modules declare indexes the way they declare
// migrations and the kernel applies them at boot. Inside a module, only store/ may import this
// package (tools/archlint).
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

// DB is the server's Mongo handle.
type DB struct {
	client   *mongo.Client
	database *mongo.Database
}

// Options configures the connection.
type Options struct {
	URI      string
	Database string
}

// Index is one index a module wants on one of its collections.
//
// It is spelled without any driver type so that a module's module.go can declare its indexes
// without importing this package, which tools/archlint reserves for store/ packages.
type Index struct {
	// Collection must start with the owning module's ID and an underscore.
	Collection string

	// Name identifies the index. Always set it: an unnamed index gets a generated name derived
	// from its keys, which makes it awkward to talk about and awkward to drop.
	Name string

	// Unique makes the index reject duplicates. On an append-only feed this is usually wrong; on
	// an idempotency key it is the whole point.
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

// Key is one field in an index.
type Key struct {
	// Field is the document field name, as stored (so the bson tag, not the Go field name).
	Field string

	// Descending sorts the index the other way. Set it for the timestamp of a feed read
	// newest-first: an ascending index can serve that query, but only by walking it backwards.
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
		// Guarded, never unconditional: SetExpireAfterSeconds(0) means "expire the moment the date
		// passes", so an unset duration would turn every index into one that empties its collection.
		opts = opts.SetExpireAfterSeconds(int32(ix.ExpireAfter.Seconds()))
	}

	return mongo.IndexModel{Keys: keys, Options: opts}
}

// Open connects to Mongo and verifies the connection is usable.
func Open(ctx context.Context, opts Options) (*DB, error) {
	client, err := mongo.Connect(options.Client().
		ApplyURI(opts.URI).
		// The driver defaults to 30 seconds, and a handler runs on the publisher's goroutine, so a
		// stalled Mongo lands that wait on a sign-in. Five not two: a failover is not an outage.
		SetServerSelectionTimeout(5 * time.Second))
	if err != nil {
		return nil, fmt.Errorf("connect to mongo: %w", err)
	}

	// Connect is lazy, so without this the first sign of a bad URI or an unreachable host is a
	// failed query somewhere in a request, long after boot reported success.
	if err := client.Ping(ctx, nil); err != nil {
		_ = client.Disconnect(ctx)
		return nil, fmt.Errorf("ping mongo: %w", err)
	}

	return &DB{client: client, database: client.Database(opts.Database)}, nil
}

// Ping answers whether the server responds, without touching any collection.
func (db *DB) Ping(ctx context.Context) error {
	return db.client.Ping(ctx, nil)
}

// Collection returns a handle to one of a module's collections.
func (db *DB) Collection(name string) *mongo.Collection {
	return db.database.Collection(name)
}

// EnsureIndexes creates the indexes a module declared, and refuses any collection that is not the
// module's own.
//
// This is the one namespacing rule in the project a machine can actually enforce. The SQL Server
// side has the same convention and no way to check it, because table names are buried in SQL
// strings that only the database ever parses. Here every collection name passes through this
// function, so a typo or a reach into someone else's data fails the boot instead of the review.
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

// Close releases the connection pool.
func (db *DB) Close(ctx context.Context) error {
	return db.client.Disconnect(ctx)
}

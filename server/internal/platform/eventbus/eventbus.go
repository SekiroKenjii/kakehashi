// Package eventbus is the in-process, typed pub/sub the modules use to talk to each other without
// knowing each other.
//
// It exists so a module can announce something happened without holding a reference to whoever
// cares. When identity records a sign-in, it publishes SignedIn; whether that appends to an
// activity feed, invalidates a cache, or falls on an empty room is not the identity module's
// problem.
//
// Reach for an event when the publisher does not need an answer. When it does need one ("is this
// module assigned to that account?"), use a service interface from the module's api package
// instead. Events are announcements, not requests.
//
// This is deliberately not a message queue. Events do not survive a restart and do not cross
// process boundaries; they decouple modules that are compiled together. Anything that must survive
// a crash belongs in a table, written in the same transaction as the fact it describes.
package eventbus

import (
	"context"
	"log/slog"
	"reflect"
	"sync"
)

// Bus fans events out to the handlers subscribed to their type.
//
// Delivery is synchronous: Publish returns once every handler has run, on the publisher's
// goroutine. That keeps causality easy to follow, and it keeps the event inside the caller's
// request context, so a handler's work is traced and cancelled along with the request that caused
// it. Handlers must therefore stay quick; anything slow belongs on a goroutine the handler spawns
// itself, with a context that is not the request's.
type Bus struct {
	log *slog.Logger

	mu       sync.RWMutex
	handlers map[reflect.Type][]any
}

// New returns an empty bus. log records handler panics.
func New(log *slog.Logger) *Bus {
	return &Bus{
		log:      log,
		handlers: make(map[reflect.Type][]any),
	}
}

// Subscribe registers fn to receive every event of type E.
//
// Subscriptions are permanent: there is no Unsubscribe, because modules live as long as the
// process does. Subscribe from a module's Register, never from a request handler, or a busy server
// grows a handler list without bound.
func Subscribe[E any](b *Bus, fn func(context.Context, E)) {
	t := reflect.TypeFor[E]()

	b.mu.Lock()
	defer b.mu.Unlock()

	b.handlers[t] = append(b.handlers[t], fn)
}

// Publish delivers e to every handler subscribed to E, in subscription order.
//
// A handler that panics is logged and skipped: one misbehaving listener must not take down the
// request that merely announced a fact, nor stop the listeners queued behind it.
func Publish[E any](b *Bus, ctx context.Context, e E) {
	t := reflect.TypeFor[E]()

	b.mu.RLock()
	hs := b.handlers[t]
	// Copy under the read lock: a handler is free to subscribe to something else while running,
	// and we must not iterate a slice being appended to.
	hs = append([]any(nil), hs...)
	b.mu.RUnlock()

	for _, h := range hs {
		deliver(b.log, ctx, h.(func(context.Context, E)), e, t)
	}
}

// deliver is a package-level function rather than a method because Go does not allow methods to
// declare their own type parameters.
func deliver[E any](
	log *slog.Logger, ctx context.Context, fn func(context.Context, E), e E, t reflect.Type,
) {
	defer func() {
		if r := recover(); r != nil {
			log.ErrorContext(ctx, "event handler panicked",
				"event", t.String(),
				"panic", r,
			)
		}
	}()
	fn(ctx, e)
}

// Package notes is the reference feature module: a full vertical slice from the database to the
// wire.
//
// Copy this directory to start a new module. What each layer is for:
//
//	api/      the contract. Interfaces, DTOs, events. The only package other modules may import,
//	          and the only one that may not import them.
//	domain/   entities and the rules they enforce. Knows nothing about SQL or protobuf, which is
//	          what makes it testable in isolation.
//	store/    persistence. Owns every table prefixed notes_.
//	service/  use cases. Orchestrates domain and store, publishes events.
//	rpc/      the wire. The only package that may import generated code.
//	module.go the wiring below.
package notes

import (
	"__GO_MODULE__/server/internal/app"
	notesapi "__GO_MODULE__/server/internal/modules/notes/api"
	"__GO_MODULE__/server/internal/modules/notes/rpc"
	"__GO_MODULE__/server/internal/modules/notes/service"
	"__GO_MODULE__/server/internal/modules/notes/store"
)

// Module is the notes feature.
type Module struct {
	svc *service.Service
}

// New returns the module, ready to be mounted on the kernel.
func New() *Module { return &Module{} }

// ID namespaces the module's tables (notes_*) and its configuration keys.
func (m *Module) ID() string { return "notes" }

// Migrations hands the kernel this module's schema. The kernel applies whatever has not run yet,
// in order, before any module starts.
//
// The loop converts between two identical-looking types on purpose: returning the store's own type
// would mean naming it here, and tools/archlint reserves the database packages for store/.
func (m *Module) Migrations() []app.Migration {
	src := store.Migrations()

	out := make([]app.Migration, 0, len(src))
	for _, mg := range src {
		out = append(out, app.Migration{Name: mg.Name, SQL: mg.SQL})
	}
	return out
}

// Register builds the service and publishes it under the api interface.
func (m *Module) Register(k *app.Kernel) error {
	m.svc = service.New(store.New(k.SQL), k.Bus, nil)

	app.Provide[notesapi.Service](k, m.svc)
	return nil
}

// Routes contributes the RPC service.
func (m *Module) Routes(k *app.Kernel) []app.Route {
	pattern, handler := rpc.NewRoute(m.svc, k.RPC)

	// The ordinary case: gated on notes.access, so the pane's lock and the server's refusal read
	// the same row.
	return []app.Route{
		{Pattern: pattern, Handler: handler, Policy: app.ModuleAccess()},
	}
}

var (
	_ app.Module           = (*Module)(nil)
	_ app.Migrator         = (*Module)(nil)
	_ app.RouteContributor = (*Module)(nil)
)

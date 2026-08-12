// Package notes is the reference feature module: copy this directory to start a new one.
//
//	api/      the contract. The only package other modules may import, and the only one that may
//	          not import them.
//	domain/   entities and the rules they enforce. No SQL, no protobuf, which is what makes it
//	          testable in isolation.
//	store/    persistence. Owns every table prefixed notes_.
//	service/  use cases. Orchestrates domain and store, publishes events.
//	rpc/      the wire. The only package that may import generated code.
package notes

import (
	"github.com/SekiroKenjii/kakehashi/server/internal/app"
	notesapi "github.com/SekiroKenjii/kakehashi/server/internal/modules/notes/api"
	"github.com/SekiroKenjii/kakehashi/server/internal/modules/notes/rpc"
	"github.com/SekiroKenjii/kakehashi/server/internal/modules/notes/service"
	"github.com/SekiroKenjii/kakehashi/server/internal/modules/notes/store"
)

type Module struct {
	svc *service.Service
}

func New() *Module { return &Module{} }

// ID namespaces the module's tables (notes_*) and its configuration keys.
func (m *Module) ID() string { return "notes" }

// The kernel applies whatever has not run yet, in order, before any module starts.
//
// The loop converts between two identical-looking types on purpose: naming the store's own type
// here would pull in a database package, which tools/archlint reserves for store/.
func (m *Module) Migrations() []app.Migration {
	src := store.Migrations()

	out := make([]app.Migration, 0, len(src))
	for _, mg := range src {
		out = append(out, app.Migration{Name: mg.Name, SQL: mg.SQL})
	}
	return out
}

func (m *Module) Register(k *app.Kernel) error {
	m.svc = service.New(store.New(k.SQL), k.Bus, nil)

	app.Provide[notesapi.Service](k, m.svc)
	return nil
}

func (m *Module) Routes(k *app.Kernel) []app.Route {
	pattern, handler := rpc.NewRoute(m.svc, k.RPC)

	// Gated on notes.access, so the pane's lock and the server's refusal read the same row.
	return []app.Route{
		{Pattern: pattern, Handler: handler, Policy: app.ModuleAccess()},
	}
}

var (
	_ app.Module           = (*Module)(nil)
	_ app.Migrator         = (*Module)(nil)
	_ app.RouteContributor = (*Module)(nil)
)

// Package plugins is the catalog a deployment offers and the artifacts behind it.
//
// A plugin is an assembly set built somewhere else, so everything this module does is in service of
// one decision a client has to make before running it: is this what it says it is. The catalog says
// what is on offer, the artifact carries its own digest, and what the client does with the answer
// is the client's business — nothing stored here is read as authorization.
//
// The layers are the ones every module has:
//
//	api/      the contract. Interfaces, DTOs, events. The only package other modules may import.
//	domain/   the plugin aggregate and the rules an identity, a version and a digest obey.
//	store/    persistence. Owns every table in the plugins schema.
//	service/  use cases, split by the family a caller reaches for together.
//	rpc/      the wire. The only package that may import generated code.
//	module.go the wiring below.
package plugins

import (
	"__GO_MODULE__/server/internal/app"
	pluginsapi "__GO_MODULE__/server/internal/modules/plugins/api"
	"__GO_MODULE__/server/internal/modules/plugins/rpc"
	"__GO_MODULE__/server/internal/modules/plugins/service"
	"__GO_MODULE__/server/internal/modules/plugins/store"
)

// Module is the plugin catalog.
type Module struct {
	svc *service.Service
}

// New returns the module, ready to be mounted on the kernel.
func New() *Module { return &Module{} }

// ID namespaces the module's tables (the plugins schema) and its configuration keys.
func (m *Module) ID() string { return "plugins" }

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

	app.Provide[pluginsapi.Service](k, m.svc)
	return nil
}

// Routes contributes the two RPC services.
//
// They are two routes rather than one because they are two policies. Reading the catalog is
// ordinary module access; changing what it offers is a permission of its own, and a single route
// would have to gate the write surface in a handler — which is the wrapper somebody forgets.
func (m *Module) Routes(k *app.Kernel) []app.Route {
	catalog, catalogHandler := rpc.NewRoute(m.svc, k.RPC)
	admin, adminHandler := rpc.NewAdminRoute(m.svc, k.RPC)

	return []app.Route{
		{Pattern: catalog, Handler: catalogHandler, Policy: app.ModuleAccess()},
		{
			Pattern: admin,
			Handler: adminHandler,
			Policy:  app.Permission(pluginsapi.PermissionManagePlugins),
		},
	}
}

var (
	_ app.Module           = (*Module)(nil)
	_ app.Migrator         = (*Module)(nil)
	_ app.RouteContributor = (*Module)(nil)
)

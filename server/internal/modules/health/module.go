// Package health is the smallest possible module, and the one to read first.
//
// It owns no tables, stores nothing, and depends on no other module, so what is left is exactly
// the skeleton every other module shares:
//
//	api/      the contract. Interfaces and DTOs. The only package other modules may import.
//	service/  use cases. Where the rules would live, if this module had any.
//	rpc/      the wire. The only package allowed to import generated protobuf code.
//	module.go the wiring below.
//
// A module with data adds domain/ (entities and their invariants) and store/ (persistence, owning
// every table prefixed with the module's ID). Copy notes/ for that shape.
package health

import (
	"github.com/SekiroKenjii/kakehashi/server/internal/app"
	healthapi "github.com/SekiroKenjii/kakehashi/server/internal/modules/health/api"
	"github.com/SekiroKenjii/kakehashi/server/internal/modules/health/rpc"
	"github.com/SekiroKenjii/kakehashi/server/internal/modules/health/service"
)

// Module is the health feature.
type Module struct {
	svc *service.Service
}

func New() *Module { return &Module{} }

// ID namespaces the module. Health owns no storage, so nothing here is prefixed with it yet.
func (m *Module) ID() string { return "health" }

// Register builds the service and publishes it under the api interface.
func (m *Module) Register(k *app.Kernel) error {
	m.svc = service.New(nil)

	app.Provide[healthapi.Service](k, m.svc)
	return nil
}

// Routes contributes the RPC service and the liveness probe.
func (m *Module) Routes(k *app.Kernel) []app.Route {
	pattern, handler := rpc.NewRoute(m.svc, k.RPC)

	// Public, both of them. A liveness probe that needs an account is not a liveness probe, and
	// the version endpoint answers the same question a probe does.
	return []app.Route{
		{Pattern: pattern, Handler: handler, Policy: app.Public()},
		{Pattern: "GET /healthz", Handler: rpc.NewLiveness(), Policy: app.Public()},
	}
}

var (
	_ app.Module           = (*Module)(nil)
	_ app.RouteContributor = (*Module)(nil)
)

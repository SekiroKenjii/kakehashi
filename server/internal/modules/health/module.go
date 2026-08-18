// Package health is the smallest possible module, and the one to read first.
//
// It owns no tables and depends on no other module, so what is left is exactly the skeleton every
// other module shares:
//
//	api/      the contract. Interfaces and DTOs. The only package other modules may import.
//	service/  use cases. Where the rules would live, if this module had any.
//	store/    persistence. Here it persists nothing: it holds the probes System answers with,
//	          because touching a database — even to ping it — is store/'s alone.
//	rpc/      the wire. The only package allowed to import generated protobuf code.
//	module.go the wiring below.
//
// A module with data adds domain/ (entities and their invariants) and gives store/ tables prefixed
// with the module's ID. Copy notes/ for that shape.
package health

import (
	"__GO_MODULE__/server/internal/app"
	healthapi "__GO_MODULE__/server/internal/modules/health/api"
	"__GO_MODULE__/server/internal/modules/health/rpc"
	"__GO_MODULE__/server/internal/modules/health/service"
	"__GO_MODULE__/server/internal/modules/health/store"
)

// Module is the health feature.
type Module struct {
	version string
	svc     *service.Service
}

// New returns the module, ready to be mounted on the kernel. version is what System reports the
// binary as; the composition root owns it because only the build that made the binary knows.
func New(version string) *Module { return &Module{version: version} }

// ID namespaces the module. Health owns no storage, so nothing here is prefixed with it yet.
func (m *Module) ID() string { return "health" }

// Register builds the service and publishes it under the api interface.
func (m *Module) Register(k *app.Kernel) error {
	m.svc = service.New(nil, m.version, store.New(k.SQL, k.Mongo))

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

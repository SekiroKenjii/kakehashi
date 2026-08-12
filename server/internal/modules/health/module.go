// Package health is the skeleton every module shares:
//
//	api/      the contract. The only package other modules may import.
//	service/  use cases.
//	rpc/      the wire. The only package allowed to import generated protobuf code.
//
// A module with data adds domain/ and store/, which owns every table prefixed with the module's ID.
// Copy notes/ for that shape.
package health

import (
	"github.com/SekiroKenjii/kakehashi/server/internal/app"
	healthapi "github.com/SekiroKenjii/kakehashi/server/internal/modules/health/api"
	"github.com/SekiroKenjii/kakehashi/server/internal/modules/health/rpc"
	"github.com/SekiroKenjii/kakehashi/server/internal/modules/health/service"
)

type Module struct {
	svc *service.Service
}

func New() *Module { return &Module{} }

func (m *Module) ID() string { return "health" }

func (m *Module) Register(k *app.Kernel) error {
	m.svc = service.New(nil)

	app.Provide[healthapi.Service](k, m.svc)
	return nil
}

func (m *Module) Routes(k *app.Kernel) []app.Route {
	pattern, handler := rpc.NewRoute(m.svc, k.RPC)

	// Both public: a probe that needs an account is not a probe, and Ping answers the same question.
	return []app.Route{
		{Pattern: pattern, Handler: handler, Policy: app.Public()},
		{Pattern: "GET /healthz", Handler: rpc.NewLiveness(), Policy: app.Public()},
	}
}

var (
	_ app.Module           = (*Module)(nil)
	_ app.RouteContributor = (*Module)(nil)
)

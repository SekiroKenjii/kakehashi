package app

import (
	"context"
	"errors"
	"fmt"
	"log/slog"
	"time"

	"github.com/SekiroKenjii/kakehashi/server/internal/platform/config"
	"github.com/SekiroKenjii/kakehashi/server/internal/platform/database"
	"github.com/SekiroKenjii/kakehashi/server/internal/platform/eventbus"
	"github.com/SekiroKenjii/kakehashi/server/internal/platform/mongodb"
	"github.com/SekiroKenjii/kakehashi/server/internal/platform/telemetry"
)

// Everything a running server needs, acquired in one place.
//
// This used to be the body of main.run, which meant the composition root held two unrelated jobs:
// naming the modules, and knowing how to open a database. Only the first is a decision about this
// product; the second is the same for every build and belongs where the kernel lives.
//
// What it buys beyond tidiness is the cleanup stack. The version this replaces returned early on
// each acquisition failure, so a Mongo connection that could not be opened left an already-open
// SQL pool and a telemetry exporter behind. That was survivable only because the process exited
// immediately afterwards — which stopped being true the moment a test wanted to boot a server.

// BootOptions is what a caller must decide. Everything else comes from the environment.
type BootOptions struct {
	// Log receives everything from configuration onwards. Required.
	Log *slog.Logger

	// Modules are mounted in this order, which decides migration order and the reverse order they
	// stop in — and nothing else.
	Modules []Module

	// UnprotectedRouteModules are the modules permitted to serve a route that checks no permission.
	// See Kernel.AllowUnprotectedRoutes.
	UnprotectedRouteModules []string
}

// Runtime is a booted server: its configuration, its kernel, and the handles to release.
//
// It does not serve. Turning routes into a listener is internal/app/server's job, and keeping the
// two apart is what lets a test boot the whole thing and drive it through httptest without opening
// a port.
type Runtime struct {
	Log    *slog.Logger
	Cfg    *config.Config
	Kernel *Kernel

	// cleanup is unwound last-acquired-first by Close. A stack rather than a list of named steps,
	// because the order is not a policy to be maintained — it is the acquisition order, reversed,
	// and a stack cannot drift from it.
	cleanup []cleanupStep
}

type cleanupStep struct {
	name string
	fn   func(context.Context) error
}

// Boot acquires everything, mounts the modules and runs the kernel's staged boot.
//
// On failure it releases what it had already acquired and returns that with the cause, so a caller
// that gets an error owns nothing.
func Boot(ctx context.Context, opts BootOptions) (*Runtime, error) {
	if opts.Log == nil {
		return nil, errors.New("app: BootOptions.Log is required")
	}

	rt := &Runtime{Log: opts.Log}

	cfg, err := config.Load()
	if err != nil {
		return nil, err
	}
	rt.Cfg = cfg

	// Telemetry first, so the traces and metrics describing a failed startup have somewhere to go.
	shutdownTelemetry, err := telemetry.Setup(ctx, telemetry.Options{
		ServiceName: cfg.Telemetry.ServiceName,
		Enabled:     cfg.Telemetry.Enabled,
	})
	if err != nil {
		return nil, err
	}
	rt.push("telemetry", shutdownTelemetry)

	sqlDB, err := database.Open(ctx, database.Options{
		DSN:          cfg.SQLServer.DSN,
		MaxOpenConns: cfg.SQLServer.MaxOpenConns,
	})
	if err != nil {
		return nil, rt.unwind(ctx, err)
	}
	rt.push("sql", func(context.Context) error { return sqlDB.Close() })

	mongoDB, err := mongodb.Open(ctx, mongodb.Options{
		URI:      cfg.Mongo.URI,
		Database: cfg.Mongo.Database,
	})
	if err != nil {
		return nil, rt.unwind(ctx, err)
	}
	rt.push("mongo", mongoDB.Close)

	kernel := NewKernel(opts.Log, cfg, sqlDB, mongoDB, eventbus.New(opts.Log))
	kernel.Mount(opts.Modules...)
	kernel.AllowUnprotectedRoutes(opts.UnprotectedRouteModules...)
	rt.Kernel = kernel

	// Pushed before Boot rather than after: Boot stops what it started on its own failure path, and
	// Shutdown is safe to call on a kernel that started nothing.
	rt.push("modules", kernel.Shutdown)

	if err := kernel.Boot(ctx); err != nil {
		return nil, rt.unwind(ctx, err)
	}

	opts.Log.InfoContext(ctx, "booted",
		"app", Name,
		"version", Version,
		"commit", Commit,
		"addr", cfg.Addr,
		"public_url", cfg.PublicURL,
		"modules", len(opts.Modules),
	)
	return rt, nil
}

// Close releases everything Boot acquired, last first.
//
// Every step runs even when an earlier one fails, and every failure is reported: a store refusing
// to close must not strand the exporter behind it, and the first error must not hide the rest.
//
// The steps share ctx's deadline but not equally — each gets an equal slice of whatever is left, so
// one step that hangs cannot spend the whole budget and hand the next an already-expired context.
// That was the old shape, and it meant a slow module stop reliably lost the telemetry flush
// explaining why it was slow.
func (r *Runtime) Close(ctx context.Context) error {
	var errs []error

	for i := len(r.cleanup) - 1; i >= 0; i-- {
		step := r.cleanup[i]

		stepCtx, cancel := context.WithTimeout(ctx, r.share(i+1))
		if err := step.fn(stepCtx); err != nil {
			errs = append(errs, fmt.Errorf("close %s: %w", step.name, err))
		}
		cancel()
	}

	r.cleanup = nil
	return errors.Join(errs...)
}

// share is how long one of remaining steps may take: an equal slice of the time left, so finishing
// early hands the surplus to whatever comes next.
func (r *Runtime) share(remaining int) time.Duration {
	const floor = time.Second

	budget := r.Cfg.ShutdownTimeout
	if budget <= 0 {
		budget = 15 * time.Second
	}
	if remaining < 1 {
		remaining = 1
	}

	slice := budget / time.Duration(remaining)
	if slice < floor {
		return floor
	}
	return slice
}

func (r *Runtime) push(name string, fn func(context.Context) error) {
	r.cleanup = append(r.cleanup, cleanupStep{name: name, fn: fn})
}

// unwind releases what was acquired before a failure and returns the cause with whatever the
// release itself reported.
//
// It runs on a context detached from the caller's: a boot cancelled by a signal must still be able
// to close a pool, and passing the cancelled context would make every step fail instantly.
func (r *Runtime) unwind(ctx context.Context, cause error) error {
	closeCtx, cancel := context.WithTimeout(context.WithoutCancel(ctx), shutdownGrace)
	defer cancel()

	return errors.Join(cause, r.Close(closeCtx))
}

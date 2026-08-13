// Command server is the entry point and the composition root.
//
// It names two things and nothing else: every module this build ships, and the modules permitted to
// serve a route that checks no permission. Acquiring the datastores, booting the kernel and serving
// are internal/app's job; what a module IS — its tables, its permissions, its screen and what
// protects it — is the module's own.
//
// Neither list grows because a module was added. modules() gains one line; the other changes only
// when somebody decides to exempt a route from the permission check, which is a decision worth
// seeing in a diff.
package main

import (
	"context"
	"errors"
	"log/slog"
	"os"
	"os/signal"
	"syscall"

	"github.com/SekiroKenjii/kakehashi/server/internal/app"
	"github.com/SekiroKenjii/kakehashi/server/internal/app/server"
	"github.com/SekiroKenjii/kakehashi/server/internal/modules/account"
	"github.com/SekiroKenjii/kakehashi/server/internal/modules/activity"
	"github.com/SekiroKenjii/kakehashi/server/internal/modules/authz"
	"github.com/SekiroKenjii/kakehashi/server/internal/modules/health"
	"github.com/SekiroKenjii/kakehashi/server/internal/modules/navigation"
	"github.com/SekiroKenjii/kakehashi/server/internal/modules/notes"
	"github.com/SekiroKenjii/kakehashi/server/internal/platform/logging"
)

func main() {
	log := logging.FromEnv()

	// The context is cancelled on SIGINT or SIGTERM, which is what a container runtime sends
	// before it resorts to SIGKILL. Everything downstream treats cancellation as "wind up", so
	// this one line is the whole graceful-shutdown trigger.
	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt, syscall.SIGTERM)
	defer stop()

	// Default signal handling is restored the moment the first signal arrives, so a second Ctrl-C
	// during a slow shutdown kills the process instead of being swallowed by a still-installed
	// handler.
	go func() {
		<-ctx.Done()
		stop()
	}()

	if err := run(ctx, log); err != nil {
		log.Error("server stopped", "error", err)
		os.Exit(1)
	}
	log.Info("server stopped")
}

// run boots the server, serves it, and releases what the boot acquired.
//
// Three statements, and the order of the last two is the point: Run returns only once the requests
// in flight have finished, and Close is what shuts the modules and stores those requests were
// using. Reversed, shutdown would close a database underneath a handler still reading from it.
func run(ctx context.Context, log *slog.Logger) error {
	rt, err := app.Boot(ctx, app.BootOptions{
		Log:                     log,
		Modules:                 modules(),
		UnprotectedRouteModules: unprotectedRouteModules,
	})
	if err != nil {
		return err
	}

	serveErr := server.New(rt.Kernel).Run(ctx)

	// Detached from ctx, which cancellation has already closed: the cleanup needs a live context to
	// do its work in.
	closeCtx, cancel := context.WithTimeout(context.WithoutCancel(ctx), rt.Cfg.ShutdownTimeout)
	defer cancel()

	// Joined rather than returned separately, so a serve failure never skips the cleanup:
	// returning early would leak the pools and drop the telemetry that explains the failure.
	return errors.Join(serveErr, rt.Close(closeCtx))
}

// modules builds the mount list. It is the only function that names every module, and the only
// thing in this file that grows when one is added: one line, and nothing else about it.
//
// Order decides two things and no more: the order migrations run in, and the reverse order modules
// stop in. Which module can see which service does not depend on it — that is what the kernel's
// staged boot is for.
//
// A function rather than a literal inside run(), so a test can walk it without booting a server.
func modules() []app.Module {
	return []app.Module{
		health.New(),
		notes.New(),
		account.New(),
		activity.New(),
		authz.New(),
		navigation.New(),
	}
}

// unprotectedRouteModules are the modules permitted to serve a route whose policy checks no
// permission. Boot refuses Public or SignedIn from any other module.
//
// It lives at the composition root because exemption is a security decision a module must not make
// about itself, and it grows per exemption, never per added module. What earns a place, and why
// each of the four has one: docs/adr/0001-per-route-permission-policy.md.
var unprotectedRouteModules = []string{"health", "account", "authz", "navigation"}

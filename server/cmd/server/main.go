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
//
// The kakehashi: markers below delimit the wiring a generator writes and a removable unit takes
// back: docs/BOILERPLATE.md.
package main

import (
	"context"
	"errors"
	"log/slog"
	"os"
	"os/signal"
	"syscall"

	"__GO_MODULE__/server/internal/app"
	"__GO_MODULE__/server/internal/app/server"
	// kakehashi:module-imports:begin
	"__GO_MODULE__/server/internal/modules/account"
	// kakehashi:unit-activity:begin
	"__GO_MODULE__/server/internal/modules/activity"
	// kakehashi:unit-activity:end
	"__GO_MODULE__/server/internal/modules/authz"
	"__GO_MODULE__/server/internal/modules/health"
	"__GO_MODULE__/server/internal/modules/navigation"
	// kakehashi:unit-notes:begin
	"__GO_MODULE__/server/internal/modules/notes"
	// kakehashi:unit-notes:end
	// kakehashi:module-imports:end
	"__GO_MODULE__/server/internal/platform/logging"
)

func main() {
	log := logging.FromEnv()

	// Cancelled on SIGINT or SIGTERM, which a container runtime sends before SIGKILL. Everything
	// downstream treats cancellation as "wind up", so this line is the whole shutdown trigger.
	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt, syscall.SIGTERM)
	defer stop()

	// Default handling is restored on the first signal, so a second Ctrl-C during a slow shutdown
	// kills the process rather than being swallowed.
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
		// kakehashi:module-registrations:begin
		health.New(),
		// kakehashi:unit-notes:begin
		notes.New(),
		// kakehashi:unit-notes:end
		account.New(),
		// kakehashi:unit-activity:begin
		activity.New(),
		// kakehashi:unit-activity:end
		authz.New(),
		navigation.New(),
		// kakehashi:module-registrations:end
	}
}

// unprotectedRouteModules are the modules permitted to serve a route whose policy checks no
// permission. Boot refuses Public or SignedIn from any other module.
//
// It lives at the composition root because exemption is a security decision a module must not make
// about itself, and it grows per exemption, never per added module. What earns a place, and why
// each of the four has one: docs/adr/0001-per-route-permission-policy.md.
var unprotectedRouteModules = []string{"health", "account", "authz", "navigation"}

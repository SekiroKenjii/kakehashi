// Command server is the composition root. It names two things and nothing else: every module this
// build ships, and the modules permitted to serve a route that checks no permission. Acquiring the
// datastores, booting the kernel and serving are internal/app's job.
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

	// Everything downstream treats cancellation as "wind up", so this one line is the whole
	// graceful-shutdown trigger. SIGTERM is what a container runtime sends before SIGKILL.
	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt, syscall.SIGTERM)
	defer stop()

	// Restores default signal handling the moment the first signal arrives, so a second Ctrl-C
	// during a slow shutdown kills the process. Deferring stop() alone kept the handler installed
	// for the whole shutdown window, which swallowed exactly the signal somebody sends because the
	// shutdown is taking too long.
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

// The order of the last two statements is the point: Run returns only once the requests in flight
// have finished, and Close is what shuts the modules and stores those requests were using.
// Reversed, shutdown would close a database underneath a handler still reading from it.
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

	// Joined rather than returned separately, so a serve failure never costs the cleanup. The
	// version this replaces returned the serve error immediately and skipped every step below it,
	// leaking the pools and dropping the telemetry that explained the failure.
	return errors.Join(serveErr, rt.Close(closeCtx))
}

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
// permission. The kernel refuses at boot any route declaring Public or SignedIn from a module that
// is not named here.
//
// Each of the four would break the server without it:
//
//	health       a liveness probe that needs an account is not a liveness probe.
//	account      signing in cannot require a permission you can only have after signing in, and
//	             OpenID Connect has to answer an anonymous browser.
//	authz        a module that answers "what may I do" cannot require permission to answer.
//	navigation   a client cannot draw a locked door until it knows the door is there, so an account
//	             with no grants must still be able to ask what its pane looks like.
//
// Being on this list buys a module permission to ASK for an unprotected route, not blanket
// exemption — the difference from the list it replaces, where naming a module here removed the
// check from every route it served, so the account module's user directory was protected only by a
// wrapper somebody had written by hand.
//
// Exemption is a security decision, so it is named here rather than declared by a module about
// itself — see Kernel.AllowUnprotectedRoutes. It grows per security exemption, not per module.
var unprotectedRouteModules = []string{"health", "account", "authz", "navigation"}

package app

// Identity of the application, fixed at compile time.
const (
	// ID namespaces anything that needs a stable machine-readable name.
	ID = "__APP_NAME_LOWER__"

	// Name is for humans: logs, telemetry, the version banner.
	Name = "__APP_TITLE__"
)

// Build metadata, stamped by the linker.
//
// Stamped rather than read from a file at runtime, so a binary that has been copied to a server
// with no repository around it can still say exactly where it came from. See the Makefile's
// LDFLAGS, and the Dockerfile, which passes the same three values through as build arguments.
var (
	Version = "0.1.0-dev"
	Commit  = "unknown"
	Date    = "unknown"
)

package app

const (
	// ID is the machine-readable name; it namespaces anything needing a stable one.
	ID = "kakehashi"

	// Name is for humans: logs, telemetry, the version banner.
	Name = "Kakehashi"
)

// Stamped by the linker rather than read from a file at runtime, so a binary copied to a server
// with no repository around it can still say exactly where it came from. See the Makefile's
// LDFLAGS, and the Dockerfile, which passes the same three values through as build arguments.
var (
	Version = "0.1.0-dev"
	Commit  = "unknown"
	Date    = "unknown"
)

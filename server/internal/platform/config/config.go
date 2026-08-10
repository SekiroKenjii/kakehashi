// Package config loads the server's settings from the environment.
//
// The variables, in one place:
//
//	KAKEHASHI_ADDR                  listen address, default :8080
//	KAKEHASHI_PUBLIC_URL            externally reachable origin; becomes the OIDC issuer
//	KAKEHASHI_SHUTDOWN_TIMEOUT      how long modules get to stop, default 15s
//	KAKEHASHI_SQLSERVER_DSN         required
//	KAKEHASHI_SQLSERVER_MAX_OPEN_CONNS
//	KAKEHASHI_MONGO_URI             required
//	KAKEHASHI_MONGO_DATABASE
//	KAKEHASHI_LOG_LEVEL             debug|info|warn|error
//	KAKEHASHI_LOG_FORMAT            text to turn off JSON logging
//	KAKEHASHI_TELEMETRY_ENABLED     false to keep OpenTelemetry off entirely
//	OTEL_SERVICE_NAME               standard, not prefixed
//	OTEL_EXPORTER_OTLP_ENDPOINT     standard; absent means telemetry stays off
//
// Modules add their own under KAKEHASHI_<MODULE>_*; see Config.Module.
//
// Environment variables rather than a file, because that is what every place this is likely to run
// already speaks: docker compose, a systemd unit, a Kubernetes secret. A file would need a path,
// which would need an environment variable anyway, and would then have to be mounted into the
// container the config was meant to configure.
//
// Everything the server reads is prefixed KAKEHASHI_, with two deliberate exceptions:
// OTEL_SERVICE_NAME and OTEL_EXPORTER_OTLP_ENDPOINT are spelled the way the OpenTelemetry
// specification spells them. Renaming standard variables to match a house style is how you end up
// with a collector that silently exports nothing because the operator set the name they knew.
package config

import (
	"errors"
	"fmt"
	"os"
	"strconv"
	"strings"
	"time"
)

// EnvPrefix namespaces every variable this server owns.
const EnvPrefix = "KAKEHASHI_"

// Config is the process-wide configuration, resolved once at boot.
type Config struct {
	// Addr is the listen address, e.g. ":8080".
	Addr string

	// PublicURL is the externally reachable origin, e.g. "https://api.example.com".
	//
	// It is not cosmetic. It becomes the OpenID Connect issuer, and an issuer that does not match
	// what the client dialled is rejected by every conforming client, including this project's.
	// Behind a reverse proxy it is the proxy's address, not the container's.
	PublicURL string

	// ShutdownTimeout bounds how long modules get to stop cleanly before the process exits anyway.
	ShutdownTimeout time.Duration

	SQLServer SQLServer
	Mongo     Mongo
	Telemetry Telemetry
}

// SQLServer configures the transactional store.
type SQLServer struct {
	// DSN is a go-mssqldb connection URL, e.g.
	// "sqlserver://sa:pass@localhost:1433?database=kakehashi".
	DSN string

	// MaxOpenConns caps the pool. Unlike the desktop original, which pinned it to one connection
	// because SQLite serialises writes anyway, a server has real concurrency to serve.
	MaxOpenConns int
}

// Mongo configures the append-only store.
type Mongo struct {
	URI      string
	Database string
}

// Telemetry configures OpenTelemetry export.
type Telemetry struct {
	// ServiceName labels every span and metric. From OTEL_SERVICE_NAME.
	ServiceName string

	// Enabled reports whether traces and metrics are exported.
	//
	// Off unless an OTLP endpoint is configured, and forced off by KAKEHASHI_TELEMETRY_ENABLED=false
	// whatever else is set — which is the switch to reach for when a collector is running for
	// something else on the same machine and you want this server to stay out of it. With it off
	// the server keeps logging to the console exactly as before; nothing else changes.
	//
	// The endpoint itself is not stored: the OTLP exporters read that variable themselves, along
	// with the headers, protocol and timeout variables that go with it. Parsing it here would mean
	// reimplementing that whole family, slightly differently.
	Enabled bool
}

// Load reads the configuration, reporting every problem at once.
//
// Every problem, not the first: a fresh deployment usually has several variables missing, and
// finding them one restart at a time is a waste of everybody's afternoon.
func Load() (*Config, error) {
	l := &loader{}

	c := &Config{
		Addr:            l.str(EnvPrefix+"ADDR", ":8080"),
		PublicURL:       l.str(EnvPrefix+"PUBLIC_URL", "http://localhost:8080"),
		ShutdownTimeout: l.duration(EnvPrefix+"SHUTDOWN_TIMEOUT", 15*time.Second),

		SQLServer: SQLServer{
			DSN:          l.required(EnvPrefix + "SQLSERVER_DSN"),
			MaxOpenConns: l.integer(EnvPrefix+"SQLSERVER_MAX_OPEN_CONNS", 25),
		},

		Mongo: Mongo{
			URI:      l.required(EnvPrefix + "MONGO_URI"),
			Database: l.str(EnvPrefix+"MONGO_DATABASE", "kakehashi"),
		},

		Telemetry: Telemetry{
			ServiceName: l.str("OTEL_SERVICE_NAME", "kakehashi-server"),
			Enabled: l.boolean(EnvPrefix+"TELEMETRY_ENABLED", true) &&
				os.Getenv("OTEL_EXPORTER_OTLP_ENDPOINT") != "",
		},
	}

	if c.SQLServer.MaxOpenConns <= 0 {
		// Not clamped to a default, because a number somebody wrote means something to them.
		// database/sql reads zero as "unlimited", which is the opposite of what a person setting a
		// pool size to zero expects, and a negative is not a size at all.
		l.problems = append(l.problems, fmt.Errorf(
			"%sSQLSERVER_MAX_OPEN_CONNS must be greater than zero, got %d",
			EnvPrefix, c.SQLServer.MaxOpenConns))
	}

	if err := l.err(); err != nil {
		return nil, err
	}
	return c, nil
}

// Module returns a module's namespaced view of the environment.
//
// A module with ID "notes" reading key "PAGE_SIZE" gets KAKEHASHI_NOTES_PAGE_SIZE. The namespace
// is the module ID for the same reason its tables are prefixed with it: two modules must not be
// able to collide on a name, and the ID is the one identifier that is already unique.
//
//	sec := k.Cfg.Module(m.ID())
//	size := sec.Integer("PAGE_SIZE", 50)
//	if err := sec.Err(); err != nil {
//	    return err
//	}
func (c *Config) Module(id string) *Section {
	return &Section{
		prefix: EnvPrefix + strings.ToUpper(strings.ReplaceAll(id, "-", "_")) + "_",
		loader: &loader{},
	}
}

// Section reads one module's settings. Like Load, it accumulates errors instead of returning at
// the first one; call Err once, after reading everything.
type Section struct {
	prefix string
	*loader
}

// String returns the value of key, or def when it is unset.
func (s *Section) String(key, def string) string { return s.str(s.prefix+key, def) }

// Integer returns the value of key parsed as an int, or def when it is unset.
func (s *Section) Integer(key string, def int) int { return s.integer(s.prefix+key, def) }

// Bool returns the value of key parsed as a bool, or def when it is unset.
func (s *Section) Bool(key string, def bool) bool { return s.boolean(s.prefix+key, def) }

// Duration returns the value of key parsed as a Go duration, or def when it is unset.
func (s *Section) Duration(key string, def time.Duration) time.Duration {
	return s.duration(s.prefix+key, def)
}

// Err reports every problem found while reading this section.
func (s *Section) Err() error { return s.loader.err() }

// loader reads variables and collects the failures rather than returning them one at a time.
type loader struct {
	problems []error
}

func (l *loader) str(key, def string) string {
	if v, ok := os.LookupEnv(key); ok && v != "" {
		return v
	}
	return def
}

func (l *loader) required(key string) string {
	v := os.Getenv(key)
	if v == "" {
		l.problems = append(l.problems, fmt.Errorf("%s is required but not set", key))
	}
	return v
}

func (l *loader) integer(key string, def int) int {
	raw, ok := os.LookupEnv(key)
	if !ok || raw == "" {
		return def
	}
	v, err := strconv.Atoi(raw)
	if err != nil {
		l.problems = append(l.problems, fmt.Errorf("%s: %q is not a number", key, raw))
		return def
	}
	return v
}

func (l *loader) boolean(key string, def bool) bool {
	raw, ok := os.LookupEnv(key)
	if !ok || raw == "" {
		return def
	}
	v, err := strconv.ParseBool(raw)
	if err != nil {
		l.problems = append(l.problems, fmt.Errorf("%s: %q is not a boolean", key, raw))
		return def
	}
	return v
}

func (l *loader) duration(key string, def time.Duration) time.Duration {
	raw, ok := os.LookupEnv(key)
	if !ok || raw == "" {
		return def
	}
	v, err := time.ParseDuration(raw)
	if err != nil {
		l.problems = append(l.problems, fmt.Errorf("%s: %q is not a duration (try 15s, 2m)", key, raw))
		return def
	}
	return v
}

func (l *loader) err() error { return errors.Join(l.problems...) }

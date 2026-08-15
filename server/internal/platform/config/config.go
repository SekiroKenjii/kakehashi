// Package config loads the server's settings from the environment.
//
// The variables, in one place:
//
//	__APP_NAME_UPPER___ADDR                  listen address, default :8080
//	__APP_NAME_UPPER___PUBLIC_URL            externally reachable origin; becomes the OIDC issuer
//	__APP_NAME_UPPER___SHUTDOWN_TIMEOUT      how long modules get to stop, default 15s
//	__APP_NAME_UPPER___SQLSERVER_DSN         required
//	__APP_NAME_UPPER___SQLSERVER_MAX_OPEN_CONNS
//	__APP_NAME_UPPER___MONGO_URI             required
//	__APP_NAME_UPPER___MONGO_DATABASE
//	__APP_NAME_UPPER___LOG_LEVEL             debug|info|warn|error
//	__APP_NAME_UPPER___LOG_FORMAT            text to turn off JSON logging
//	__APP_NAME_UPPER___TELEMETRY_ENABLED     false to keep OpenTelemetry off entirely
//	OTEL_SERVICE_NAME               standard, not prefixed
//	OTEL_EXPORTER_OTLP_ENDPOINT     standard; absent means telemetry stays off
//
// Modules add their own under __APP_NAME_UPPER___<MODULE>_*; see Config.Module.
//
// Everything the server reads is prefixed __APP_NAME_UPPER___, with two deliberate exceptions:
// OTEL_SERVICE_NAME and OTEL_EXPORTER_OTLP_ENDPOINT are spelled the way the OpenTelemetry
// specification spells them, so operators and collectors that know the standard names find them.
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
const EnvPrefix = "__APP_NAME_UPPER___"

// Config is the process-wide configuration, resolved once at boot.
type Config struct {
	// Addr is the listen address, e.g. ":8080".
	Addr string

	// PublicURL is the externally reachable origin, e.g. "https://api.example.com".
	//
	// It becomes the OpenID Connect issuer, and an issuer that does not match what the client
	// dialled is rejected by every conforming client, including this project's. Behind a reverse
	// proxy it is the proxy's address, not the container's.
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
	// "sqlserver://sa:pass@localhost:1433?database=__APP_NAME_LOWER__".
	DSN string

	// MaxOpenConns caps the pool. Load rejects values below one.
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
	// Off unless an OTLP endpoint is configured, and forced off by __APP_NAME_UPPER___TELEMETRY_ENABLED=false
	// whatever else is set. Console logging is unaffected either way.
	//
	// The endpoint itself is not stored: the OTLP exporters read OTEL_EXPORTER_OTLP_ENDPOINT
	// themselves, along with the headers, protocol and timeout variables that go with it.
	Enabled bool
}

// Load reads the configuration, reporting every problem at once rather than the first: a fresh
// deployment usually has several variables missing.
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
			Database: l.str(EnvPrefix+"MONGO_DATABASE", "__APP_NAME_LOWER__"),
		},

		Telemetry: Telemetry{
			ServiceName: l.str("OTEL_SERVICE_NAME", "__APP_NAME_LOWER__-server"),
			Enabled: l.boolean(EnvPrefix+"TELEMETRY_ENABLED", true) &&
				os.Getenv("OTEL_EXPORTER_OTLP_ENDPOINT") != "",
		},
	}

	if c.SQLServer.MaxOpenConns <= 0 {
		// Rejected rather than clamped: database/sql reads zero as "unlimited", the opposite of
		// what a pool size of zero intends, and a negative is not a size at all.
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
// A module with ID "notes" reading key "PAGE_SIZE" gets __APP_NAME_UPPER___NOTES_PAGE_SIZE. The namespace
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

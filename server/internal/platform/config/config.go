// Package config loads the server's settings from the environment.
//
// Environment variables rather than a file, because that is what every place this is likely to run
// already speaks: docker compose, a systemd unit, a Kubernetes secret. A file would need a path,
// which would need an environment variable anyway.
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

const EnvPrefix = "KAKEHASHI_"

type Config struct {
	Addr string

	// PublicURL becomes the OpenID Connect issuer, and an issuer that does not match what the
	// client dialled is rejected by every conforming client, including this project's. Behind a
	// reverse proxy it is the proxy's address, not the container's.
	PublicURL string

	ShutdownTimeout time.Duration

	SQLServer SQLServer
	Mongo     Mongo
	Telemetry Telemetry
}

type SQLServer struct {
	// DSN is a go-mssqldb connection URL:
	// "sqlserver://sa:pass@localhost:1433?database=kakehashi".
	DSN string

	// MaxOpenConns caps the pool. The desktop original pinned it to one connection because SQLite
	// serialises writes anyway; a server has real concurrency to serve.
	MaxOpenConns int
}

type Mongo struct {
	URI      string
	Database string
}

type Telemetry struct {
	ServiceName string

	// Enabled is off unless an OTLP endpoint is configured, and forced off by
	// KAKEHASHI_TELEMETRY_ENABLED=false whatever else is set — the switch to reach for when a
	// collector is running for something else on the same machine.
	//
	// The endpoint itself is not stored: the OTLP exporters read that variable themselves, along
	// with the headers, protocol and timeout variables that go with it. Parsing it here would mean
	// reimplementing that whole family, slightly differently.
	Enabled bool
}

// Load reports every problem at once, not the first: a fresh deployment usually has several
// variables missing, and finding them one restart at a time wastes an afternoon.
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
		// Not clamped to a default: database/sql reads zero as "unlimited", the opposite of what a
		// person setting a pool size to zero expects, and a negative is not a size at all.
		l.problems = append(l.problems, fmt.Errorf(
			"%sSQLSERVER_MAX_OPEN_CONNS must be greater than zero, got %d",
			EnvPrefix, c.SQLServer.MaxOpenConns))
	}

	if err := l.err(); err != nil {
		return nil, err
	}
	return c, nil
}

// Module returns a module's namespaced view of the environment: module "notes" reading key
// "PAGE_SIZE" gets KAKEHASHI_NOTES_PAGE_SIZE. The namespace is the module ID for the same reason
// its tables are: two modules must not be able to collide on a name, and the ID is the one
// identifier that is already unique.
func (c *Config) Module(id string) *Section {
	return &Section{
		prefix: EnvPrefix + strings.ToUpper(strings.ReplaceAll(id, "-", "_")) + "_",
		loader: &loader{},
	}
}

// Section, like Load, accumulates errors instead of returning at the first one; call Err once,
// after reading everything.
type Section struct {
	prefix string
	*loader
}

func (s *Section) String(key, def string) string { return s.str(s.prefix+key, def) }

func (s *Section) Integer(key string, def int) int { return s.integer(s.prefix+key, def) }

func (s *Section) Bool(key string, def bool) bool { return s.boolean(s.prefix+key, def) }

func (s *Section) Duration(key string, def time.Duration) time.Duration {
	return s.duration(s.prefix+key, def)
}

func (s *Section) Err() error { return s.loader.err() }

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

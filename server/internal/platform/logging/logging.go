// Package logging builds the server's slog logger.
package logging

import (
	"log/slog"
	"os"
	"strings"
)

// Options controls how the logger is built.
type Options struct {
	// Level is one of debug, info, warn, error. An unrecognised value falls back to info rather
	// than failing: a typo in an environment variable should not stop the server from starting.
	Level string

	// JSON switches to structured output.
	//
	// Unlike a desktop app, the default here is on. A server's logs are read by a collector far
	// more often than by a human with a terminal, and text output that has to be re-parsed
	// downstream loses the structure slog went to the trouble of recording.
	JSON bool
}

// New builds a logger writing to stderr.
//
// stderr, not stdout: under a supervisor the two are usually separated, and keeping diagnostics out
// of stdout leaves that stream free for anything the process legitimately emits.
func New(opts Options) *slog.Logger {
	handlerOpts := &slog.HandlerOptions{Level: parseLevel(opts.Level)}

	var h slog.Handler
	if opts.JSON {
		h = slog.NewJSONHandler(os.Stderr, handlerOpts)
	} else {
		h = slog.NewTextHandler(os.Stderr, handlerOpts)
	}
	return slog.New(h)
}

// FromEnv builds a logger from KAKEHASHI_LOG_LEVEL and KAKEHASHI_LOG_FORMAT.
//
// It is called before configuration is loaded, because a configuration error is the first thing
// that needs logging.
func FromEnv() *slog.Logger {
	return New(Options{
		Level: os.Getenv("KAKEHASHI_LOG_LEVEL"),
		JSON:  !strings.EqualFold(os.Getenv("KAKEHASHI_LOG_FORMAT"), "text"),
	})
}

func parseLevel(s string) slog.Level {
	switch strings.ToLower(strings.TrimSpace(s)) {
	case "debug":
		return slog.LevelDebug
	case "warn", "warning":
		return slog.LevelWarn
	case "error":
		return slog.LevelError
	default:
		return slog.LevelInfo
	}
}

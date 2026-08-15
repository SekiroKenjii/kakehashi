// Package cli is the command surface: flag parsing, the order the other packages run in, and what
// reaches the terminal. Nothing here decides anything a library could decide instead.
package cli

import (
	"errors"
	"fmt"
	"os"
	"strings"

	"github.com/spf13/cobra"
)

// version is the CLI's own version, and is what a release build overrides:
//
//	go build -ldflags "-X github.com/SekiroKenjii/kakehashi/tools/cli/internal/cli.version=0.2.1"
var version = "0.1.0"

// The exit codes. A usage error is separated from a failure because a script that wraps this tool
// treats "you asked for the wrong thing" and "it did not work" differently.
const (
	exitOK = iota
	exitFailure
	exitUsage
)

// usageError is a refusal the caller can fix by typing something else, and it prints the usage.
type usageError struct{ err error }

func (e usageError) Error() string { return e.err.Error() }
func (e usageError) Unwrap() error { return e.err }

func usagef(format string, args ...any) error {
	return usageError{err: fmt.Errorf(format, args...)}
}

// Execute runs the command line and returns the process exit code.
func Execute() int {
	root := &cobra.Command{
		Use:           "kakehashi",
		Short:         "Scaffold and maintain a WinUI 3 client and Go server in one repository",
		Version:       version,
		SilenceUsage:  true,
		SilenceErrors: true,
	}
	root.SetFlagErrorFunc(func(_ *cobra.Command, err error) error { return usageError{err: err} })
	root.AddCommand(newCommand(), doctorCommand(), versionCommand())

	command, err := root.ExecuteC()
	if err == nil {
		return exitOK
	}

	fmt.Fprintln(os.Stderr, "kakehashi:", err)
	var usage usageError
	// Cobra reports an unknown command or a bad argument count as a plain error, and both are the
	// same kind of mistake as a bad flag.
	if errors.As(err, &usage) || strings.HasPrefix(err.Error(), "unknown command") ||
		strings.Contains(err.Error(), "accepts") {
		fmt.Fprintln(os.Stderr)
		fmt.Fprintln(os.Stderr, command.UsageString())
		return exitUsage
	}
	return exitFailure
}

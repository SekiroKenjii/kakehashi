package cli

import (
	"errors"
	"strings"
	"testing"

	"github.com/spf13/cobra"

	"github.com/SekiroKenjii/kakehashi/tools/cli/internal/scaffold"
)

// parse runs the command line through the `new` command's flag definitions and stops before it
// does any work, which is where every input decision is made.
func parse(t *testing.T, args ...string) (scaffold.Inputs, error) {
	t.Helper()
	opts := &options{}
	var (
		inputs scaffold.Inputs
		err    error
	)

	command := &cobra.Command{
		Use:           "new",
		Args:          cobra.MaximumNArgs(1),
		SilenceUsage:  true,
		SilenceErrors: true,
		RunE: func(c *cobra.Command, a []string) error {
			inputs, err = collect(c, a, opts)
			return nil
		},
	}
	bind(command, opts)
	command.SetArgs(args)
	if runErr := command.Execute(); runErr != nil {
		return scaffold.Inputs{}, runErr
	}
	return inputs, err
}

func TestCollectDerivesEverythingFromTheAppName(t *testing.T) {
	inputs, err := parse(t, "OrderDesk", "--module", "github.com/me/orderdesk", "--author", "Me")
	if err != nil {
		t.Fatalf("collect: %v", err)
	}

	if inputs.AppName != "OrderDesk" || inputs.AppTitle != "OrderDesk" || inputs.ProtoPackage != "orderdesk" {
		t.Errorf("inputs = %+v", inputs)
	}
	if inputs.Accent != scaffold.DefaultAccent || inputs.Auth != scaffold.AuthInApp || !inputs.WithExample {
		t.Errorf("defaults = %+v", inputs)
	}
}

func TestCollectBare(t *testing.T) {
	inputs, err := parse(t, "OrderDesk", "--module", "github.com/me/orderdesk", "--author", "Me", "--bare")
	if err != nil {
		t.Fatalf("collect: %v", err)
	}
	if inputs.WithExample {
		t.Error("--bare kept the example module")
	}
}

func TestCollectRefusals(t *testing.T) {
	cases := []struct {
		name string
		args []string
		says string
	}{
		{"no app name at all", []string{}, "wizard"},
		{"no app name with --no-input", []string{"--no-input"}, "--no-input"},
		{"no module", []string{"OrderDesk"}, "--module"},
		{"--bare against --with-example", []string{
			"OrderDesk", "--module", "github.com/me/orderdesk", "--bare", "--with-example=true",
		}, "contradict"},
		{"an app name that is not PascalCase", []string{
			"order-desk", "--module", "github.com/me/orderdesk", "--author", "Me",
		}, "--app-name"},
		{"an auth mode that is not built", []string{
			"OrderDesk", "--module", "github.com/me/orderdesk", "--author", "Me", "--auth", "none",
		}, "--auth none"},
	}
	for _, c := range cases {
		t.Run(c.name, func(t *testing.T) {
			_, err := parse(t, c.args...)
			if err == nil {
				t.Fatalf("collect accepted %s", c.name)
			}

			// Everything here is something the caller can retype, so it prints the usage and
			// leaves with the usage exit code.
			var usage usageError
			if !errors.As(err, &usage) {
				t.Errorf("error is not a usage error: %v", err)
			}
			if !strings.Contains(err.Error(), c.says) {
				t.Errorf("error %q does not mention %q", err, c.says)
			}
		})
	}
}

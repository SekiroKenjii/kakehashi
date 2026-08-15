package cli

import (
	"context"
	"fmt"
	"io"
	"os"
	"path/filepath"
	"strings"
	"time"

	"github.com/spf13/cobra"

	"github.com/SekiroKenjii/kakehashi/tools/cli/internal/checks"
	"github.com/SekiroKenjii/kakehashi/tools/cli/internal/scaffold"
	"github.com/SekiroKenjii/kakehashi/tools/cli/internal/template"
	"github.com/SekiroKenjii/kakehashi/tools/cli/internal/tui"
)

// options is the `new` command line, one field per flag.
type options struct {
	module          string
	title           string
	protoPackage    string
	accent          string
	author          string
	withExample     bool
	bare            bool
	auth            string
	templateVersion string
	templateDir     string
	dir             string
	offline         bool
	dryRun          bool
	noInput         bool
}

func newCommand() *cobra.Command {
	opts := &options{}
	command := &cobra.Command{
		Use:   "new [app-name]",
		Short: "Scaffold a project from the template",
		Long: "Scaffold a project from the template.\n\n" +
			"With no app name, this opens the wizard, which Phase 4 builds. Until then, pass the\n" +
			"name and --module.",
		Args: func(_ *cobra.Command, args []string) error {
			if len(args) > 1 {
				return usagef("new takes one app name, got %d", len(args))
			}
			return nil
		},
		SilenceUsage: true,
		RunE: func(command *cobra.Command, args []string) error {
			return runNew(command, args, opts)
		},
	}

	bind(command, opts)
	return command
}

// bind defines the flags of `new` against an options value. It is its own function so that a test
// can build the same command line and read what it collected.
func bind(command *cobra.Command, opts *options) {
	flags := command.Flags()
	flags.StringVar(&opts.module, "module", "", "Go module path (required)")
	flags.StringVar(&opts.title, "title", "", "display name (default: the app name)")
	flags.StringVar(&opts.protoPackage, "proto-package", "", "proto root package (default: the app name, lower case)")
	flags.StringVar(&opts.accent, "accent", scaffold.DefaultAccent, "accent colour")
	flags.StringVar(&opts.author, "author", "", "author (default: git config user.name)")
	flags.BoolVar(&opts.withExample, "with-example", true, "include the example module")
	flags.BoolVar(&opts.bare, "bare", false, "leave the example module out")
	flags.StringVar(&opts.auth, "auth", scaffold.AuthInApp, "sign-in mode: inapp or browser")
	flags.StringVar(&opts.templateVersion, "template-version", "", "template version (default: the newest release)")
	flags.StringVar(&opts.templateDir, "template-dir", "", "scaffold from a template checkout instead of a release")
	flags.StringVar(&opts.dir, "dir", "", "destination (default: ./<app-name lower case>)")
	flags.BoolVar(&opts.offline, "offline", false, "use the template cache and never the network")
	flags.BoolVar(&opts.dryRun, "dry-run", false, "do the whole scaffold in a temporary directory and throw it away")
	flags.BoolVar(&opts.noInput, "no-input", false, "never prompt: fail instead of opening the wizard")
}

func runNew(command *cobra.Command, args []string, opts *options) error {
	inputs, err := collect(command, args, opts)
	if err != nil {
		return err
	}

	out := command.OutOrStdout()
	log := func(format string, args ...any) {
		fmt.Fprintf(out, "  %s\n", fmt.Sprintf(format, args...))
	}

	dest, err := destination(opts.dir, inputs.AppName)
	if err != nil {
		return err
	}
	// Before the download rather than after it: a destination that cannot be written to is the
	// caller's mistake, and finding it out costs nothing here.
	if err := scaffold.CheckDestination(dest); err != nil {
		return err
	}
	if err := preflight(command.Context()); err != nil {
		return err
	}

	resolved, err := template.New(template.Client{Log: log}).Resolve(command.Context(), template.Request{
		Dir:        opts.templateDir,
		Version:    opts.templateVersion,
		Offline:    opts.offline,
		CLIVersion: version,
	})
	if err != nil {
		return err
	}

	fmt.Fprintf(out, "kakehashi: %s <%s> from template %s\n", inputs.AppName, inputs.GoModule, resolved.Version)
	if opts.dryRun {
		plan(out, inputs, dest, resolved)
	}
	result, err := scaffold.Run(scaffold.Options{
		Source:     resolved.Dir,
		Dest:       dest,
		Descriptor: resolved.Descriptor,
		Inputs:     inputs,
		Origin:     resolved.Source,
		Version:    resolved.Version,
		CLIVersion: version,
		DryRun:     opts.dryRun,
		Log:        log,
	})
	if err != nil {
		return err
	}

	report(out, result, inputs, opts.dryRun)
	return nil
}

// collect turns the command line into inputs, or explains what is missing. With no app name at all
// this is where the wizard would take over.
func collect(command *cobra.Command, args []string, opts *options) (scaffold.Inputs, error) {
	if len(args) == 0 {
		if opts.noInput {
			return scaffold.Inputs{}, usagef("--no-input needs an app name and --module")
		}
		// Phase 4 fills the wizard in. Until it does there is nothing to fall back to, so the
		// refusal comes straight back out.
		_, err := tui.Wizard()
		return scaffold.Inputs{}, usagef("%v — pass an app name and --module", err)
	}

	flags := command.Flags()
	if opts.bare && flags.Changed("with-example") && opts.withExample {
		return scaffold.Inputs{}, usagef("--bare and --with-example contradict each other")
	}
	if opts.module == "" {
		return scaffold.Inputs{}, usagef("--module is required")
	}

	inputs := scaffold.Inputs{
		AppName:      args[0],
		AppTitle:     opts.title,
		GoModule:     opts.module,
		ProtoPackage: opts.protoPackage,
		Accent:       opts.accent,
		Author:       opts.author,
		Auth:         opts.auth,
		WithExample:  opts.withExample && !opts.bare,
	}
	if inputs.Author == "" {
		inputs.Author = scaffold.GitUserName()
	}
	inputs.Derive(time.Now())

	if err := inputs.Validate(); err != nil {
		return scaffold.Inputs{}, usageError{err: err}
	}
	return inputs, nil
}

// preflight runs the required checks scaffolding itself depends on. Running them before the fetch
// is the point: the alternative is a download, an extraction and a failure.
func preflight(ctx context.Context) error {
	failed := checks.Failed(checks.Run(ctx, checks.ForScaffold()))
	if len(failed) == 0 {
		return nil
	}

	lines := make([]string, 0, len(failed))
	for _, result := range failed {
		lines = append(lines, fmt.Sprintf("  %s: %s\n    %s", result.Name, result.Detail, result.Fix))
	}
	return fmt.Errorf("the machine is missing what scaffolding needs:\n%s\n\nrun 'kakehashi doctor' for the whole list",
		strings.Join(lines, "\n"))
}

// destination is --dir, or the app name in lower case beside the working directory.
func destination(dir, appName string) (string, error) {
	if dir == "" {
		dir = strings.ToLower(appName)
	}
	return filepath.Abs(dir)
}

func report(out io.Writer, result *scaffold.Result, in scaffold.Inputs, dryRun bool) {
	fmt.Fprintf(out, "  %d files, %d substituted, %d paths renamed\n",
		result.Files, result.Substituted, result.Renamed)
	if len(result.UnitsRemoved) > 0 {
		fmt.Fprintf(out, "  removed: %s\n", strings.Join(result.UnitsRemoved, ", "))
	}
	if dryRun {
		fmt.Fprintf(out, "\n  dry run: nothing was written to %s\n", result.Dest)
		return
	}

	where := shortest(result.Dest)
	fmt.Fprintf(out, `
  %s is ready in %s

    cd %s
    docker compose up -d
    curl http://localhost:8080/healthz
    dotnet run --project "client/src/App/%s.App/%s.App.csproj" -p:Platform=x64
`, in.AppTitle, where, where, in.AppName, in.AppName)

	if !result.Committed {
		fmt.Fprintln(out, "\n  Commit before you start: git add -A && git commit -m \"chore: scaffold\"")
	}
}

// plan prints what a run would produce, for --dry-run to be readable before the counts arrive.
func plan(out io.Writer, in scaffold.Inputs, dest string, resolved *template.Resolved) {
	example := "with the example module"
	if !in.WithExample {
		example = "without the example module"
	}
	fmt.Fprintf(out, `  plan
    destination    %s
    template       %s (%s)
    go module      %s
    proto package  %s
    title, accent  %s, %s
    author, year   %s, %s
    sign-in        %s, %s
`, dest, resolved.Version, resolved.Source, in.GoModule, in.ProtoPackage,
		in.AppTitle, in.Accent, in.Author, in.Year, in.Auth, example)
}

// shortest is the destination as a reader would type it: relative to the working directory when
// that is shorter, and absolute when the relative form is a chain of parent directories.
func shortest(dest string) string {
	wd, err := os.Getwd()
	if err != nil {
		return dest
	}
	relative, err := filepath.Rel(wd, dest)
	if err != nil || strings.HasPrefix(relative, "..") {
		return dest
	}
	return relative
}

package cli

import (
	"context"
	"errors"
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
			"With no app name, this opens the wizard. A terminal that cannot prompt is told which\n" +
			"flags to pass instead.",
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
	out := command.OutOrStdout()
	inputs, err := collect(command, args, opts)
	if err != nil {
		return err
	}

	progress := tui.NewProgress(out)
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

	fmt.Fprintf(out, "kakehashi: %s <%s>\n", inputs.AppName, inputs.GoModule)
	resolved, err := template.New(template.Client{Log: log, Step: progress.Step}).Resolve(command.Context(), template.Request{
		Dir:        opts.templateDir,
		Version:    opts.templateVersion,
		Offline:    opts.offline,
		CLIVersion: version,
	})
	if err != nil {
		return err
	}

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
		Step:       progress.Step,
	})
	if err != nil {
		return err
	}
	progress.Done()

	report(out, result, inputs, resolved.Version, opts.dryRun)
	return nil
}

// collect turns the command line into inputs, or opens the wizard when there is no command line to
// speak of. Both paths end in the same Derive and the same Validate: a project answered into being
// and one passed in flags have to be the same project.
func collect(command *cobra.Command, args []string, opts *options) (scaffold.Inputs, error) {
	inputs, err := answers(command, args, opts)
	if err != nil {
		return scaffold.Inputs{}, err
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

// answers is where the inputs came from, and is the only part of collect that differs between the
// two ways of asking.
func answers(command *cobra.Command, args []string, opts *options) (scaffold.Inputs, error) {
	if len(args) == 0 {
		return wizard(opts)
	}

	flags := command.Flags()
	if opts.bare && flags.Changed("with-example") && opts.withExample {
		return scaffold.Inputs{}, usagef("--bare and --with-example contradict each other")
	}
	if opts.module == "" {
		return scaffold.Inputs{}, usagef("--module is required")
	}

	return scaffold.Inputs{
		AppName:      args[0],
		AppTitle:     opts.title,
		GoModule:     opts.module,
		ProtoPackage: opts.protoPackage,
		Accent:       opts.accent,
		Author:       opts.author,
		Auth:         opts.auth,
		WithExample:  opts.withExample && !opts.bare,
	}, nil
}

// wizard opens the seven questions, and refuses in the two cases where asking is not an option:
// --no-input, and a terminal that cannot prompt. Both refusals name the flags that answer the same
// questions, because in both cases the reader's next move is to type them.
func wizard(opts *options) (scaffold.Inputs, error) {
	if opts.noInput {
		return scaffold.Inputs{}, usagef("--no-input needs an app name and --module")
	}

	inputs, err := tui.Wizard(tui.Options{
		Author:      author(opts.author),
		Destination: func(appName string) string { return shortestDestination(opts.dir, appName) },
	})
	switch {
	case errors.Is(err, tui.ErrNoTTY):
		return scaffold.Inputs{}, usageError{err: errors.New(tui.Refusal(""))}
	case errors.Is(err, tui.ErrCancelled):
		return scaffold.Inputs{}, errCancelled
	case err != nil:
		return scaffold.Inputs{}, err
	}
	return inputs, nil
}

// errCancelled leaves with the failure code and nothing else to say: somebody who closed the wizard
// knows why it stopped, and does not need the usage printed at them.
var errCancelled = errors.New("cancelled")

func author(given string) string {
	if given != "" {
		return given
	}
	return scaffold.GitUserName()
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

// shortestDestination is the same answer as the reader would type it, for the wizard's summary to
// show before anything has been written.
func shortestDestination(dir, appName string) string {
	dest, err := destination(dir, appName)
	if err != nil {
		return dir
	}
	return shortest(dest)
}

func report(out io.Writer, result *scaffold.Result, in scaffold.Inputs, templateVersion string, dryRun bool) {
	fmt.Fprintf(out, "  %d files, %d substituted, %d paths renamed, template %s\n",
		result.Files, result.Substituted, result.Renamed, templateVersion)
	if len(result.UnitsRemoved) > 0 {
		fmt.Fprintf(out, "  removed: %s\n", strings.Join(result.UnitsRemoved, ", "))
	}
	if dryRun {
		fmt.Fprintf(out, "\n  dry run: nothing was written to %s\n", result.Dest)
		return
	}

	tui.NextSteps(out, tui.Summary{
		Title:       in.AppTitle,
		AppName:     in.AppName,
		Dir:         shortest(result.Dest),
		WithExample: in.WithExample,
		Committed:   result.Committed,
	})
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

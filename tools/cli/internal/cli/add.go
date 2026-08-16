package cli

import (
	"fmt"
	"io"
	"os"
	"strings"

	"github.com/spf13/cobra"

	"github.com/SekiroKenjii/kakehashi/tools/cli/internal/generate"
	"github.com/SekiroKenjii/kakehashi/tools/cli/internal/naming"
	"github.com/SekiroKenjii/kakehashi/tools/cli/internal/project"
)

// module is the `add module` command line.
type moduleOptions struct {
	entity   string
	icon     string
	crud     bool
	store    string
	noClient bool
	noPage   bool
	dryRun   bool
}

func addCommand() *cobra.Command {
	command := &cobra.Command{
		Use:   "add",
		Short: "Generate a module or a page into a project kakehashi made",
		Args:  cobra.NoArgs,
	}
	command.AddCommand(addModuleCommand(), addPageCommand())
	return command
}

func addModuleCommand() *cobra.Command {
	opts := &moduleOptions{}
	command := &cobra.Command{
		Use:   "module <id>",
		Short: "Generate a module across both halves, wired in and building",
		Long: "Generate a module across both halves, wired in and building.\n\n" +
			"The id is the module's package, its SQL schema and its proto directory: lower case,\n" +
			"no separators. The entity is derived from it — orders gives Order — and --entity is\n" +
			"for the words English does not inflect that way.",
		Args: func(_ *cobra.Command, args []string) error {
			if len(args) != 1 {
				return usagef("add module takes one id, for example 'kakehashi add module orders'")
			}
			return nil
		},
		SilenceUsage: true,
		RunE: func(command *cobra.Command, args []string) error {
			return runAddModule(command, args[0], opts)
		},
	}

	flags := command.Flags()
	flags.StringVar(&opts.entity, "entity", "", "the aggregate's type name (default: the singular of the id)")
	flags.StringVar(&opts.icon, "icon", naming.DefaultIcon, "navigation icon, a name from the client's vocabulary")
	flags.BoolVar(&opts.crud, "crud", true, "generate the CRUD slice end to end")
	flags.StringVar(&opts.store, "store", "sql", "where the module keeps its data")
	flags.BoolVar(&opts.noClient, "no-client", false, "generate the proto and the server half only")
	flags.BoolVar(&opts.noPage, "no-page", false, "generate the client module without a page")
	flags.BoolVar(&opts.dryRun, "dry-run", false, "print the plan and write nothing")
	return command
}

func runAddModule(command *cobra.Command, id string, opts *moduleOptions) error {
	if err := supported(opts); err != nil {
		return err
	}

	names, err := naming.New(id, opts.entity, opts.icon)
	if err != nil {
		return usageError{err: err}
	}

	working, err := os.Getwd()
	if err != nil {
		return err
	}
	p, err := project.Open(working, version)
	if err != nil {
		return err
	}

	out := command.OutOrStdout()
	result, err := generate.Add(generate.Options{
		Project: p,
		Names:   names,
		Client:  !opts.noClient,
		DryRun:  opts.dryRun,
		Log:     func(format string, args ...any) { fmt.Fprintf(out, "  %s\n", fmt.Sprintf(format, args...)) },
	})
	if err != nil {
		return err
	}

	reportModule(out, result, names, opts.dryRun)
	return nil
}

// supported refuses the flags whose shape the generator does not have. The module it writes is the
// example module with the names changed, and the example has one shape: a CRUD slice over SQL with
// a page. Refusing is the honest answer until a second example exists to derive from.
func supported(opts *moduleOptions) error {
	if !opts.crud {
		return usagef("--crud=false is not built: the generated module is derived from the example, which is a CRUD slice")
	}
	if opts.store != "sql" {
		return usagef("--store %s is not built: the generated module is derived from the example, which keeps its data in SQL Server", opts.store)
	}
	if opts.noPage {
		return usagef("--no-page is not built: the generated client module is derived from the example, which has a page")
	}
	return nil
}

func reportModule(out io.Writer, result *generate.Result, names naming.Names, dryRun bool) {
	if dryRun {
		fmt.Fprintf(out, "  plan for %s (%s)\n", names.ID, names.Entity)
		for _, file := range result.Files {
			fmt.Fprintf(out, "    create  %s\n", file)
		}
		for _, site := range result.Wiring {
			fmt.Fprintf(out, "    wire    %s\n", site)
		}
		fmt.Fprintf(out, "    record  %s\n", result.Record)
		fmt.Fprintf(out, "\n  dry run: nothing was written\n")
		return
	}

	fmt.Fprintf(out, "\n  %s is in: %d files, %d wiring sites\n", names.Module, len(result.Files), len(result.Wiring))
	fmt.Fprintf(out, "  verified: %s\n", strings.Join(result.Verified, ", "))
	if len(result.Skipped) > 0 {
		fmt.Fprintf(out, "  not verified here: %s\n", strings.Join(result.Skipped, ", "))
	}
	fmt.Fprintf(out, `
  Next, in this order:

    1. edit proto/*/%s/v1/%s.proto — the message is the example's shape, not yours
    2. buf generate
    3. put the rules in server/internal/modules/%s/domain and service
`, names.ID, names.ID, names.ID)
}

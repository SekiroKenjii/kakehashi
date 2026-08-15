package cli

import (
	"fmt"
	"os"
	"strings"

	"github.com/spf13/cobra"

	"github.com/SekiroKenjii/kakehashi/tools/cli/internal/generate"
	"github.com/SekiroKenjii/kakehashi/tools/cli/internal/project"
)

func removeCommand() *cobra.Command {
	command := &cobra.Command{
		Use:   "remove",
		Short: "Take a module back out of a project kakehashi made",
		Args:  cobra.NoArgs,
	}
	command.AddCommand(removeModuleCommand())
	return command
}

func removeModuleCommand() *cobra.Command {
	var force, dryRun bool

	command := &cobra.Command{
		Use:   "module <id>",
		Short: "Remove a module and everything that wires it in",
		Long: "Remove a module and everything that wires it in.\n\n" +
			"What comes out is what the record says went in: the one a generation left behind, or\n" +
			"the unit file the template ships for a module that came with it.",
		Args: func(_ *cobra.Command, args []string) error {
			if len(args) != 1 {
				return usagef("remove module takes one id, for example 'kakehashi remove module orders'")
			}
			return nil
		},
		SilenceUsage: true,
		RunE: func(command *cobra.Command, args []string) error {
			working, err := os.Getwd()
			if err != nil {
				return err
			}
			p, err := project.Open(working)
			if err != nil {
				return err
			}

			out := command.OutOrStdout()
			result, err := generate.Remove(generate.RemoveOptions{
				Project: p,
				ID:      args[0],
				Force:   force,
				DryRun:  dryRun,
				Log:     func(format string, a ...any) { fmt.Fprintf(out, "  %s\n", fmt.Sprintf(format, a...)) },
			})
			if err != nil {
				return err
			}

			if dryRun {
				fmt.Fprintf(out, "  plan for %s\n", args[0])
				for _, path := range result.Paths {
					fmt.Fprintf(out, "    remove  %s\n", path)
				}
				for _, file := range result.Wiring {
					fmt.Fprintf(out, "    unwire  %s\n", file)
				}
				fmt.Fprintf(out, "\n  dry run: nothing was removed\n")
				return nil
			}

			fmt.Fprintf(out, "\n  %s is out: %d paths, %d files unwired\n",
				args[0], len(result.Paths), len(result.Wiring))
			// The database is the one thing a generator cannot take back, because it is not in the
			// repository and somebody else may be using it.
			fmt.Fprintf(out, `
  Its tables are still in any database it has migrated. When you are sure:

    %s
`, result.Schema)
			return nil
		},
	}

	flags := command.Flags()
	flags.BoolVar(&force, "force", false, "remove even though the working tree has other changes in it")
	flags.BoolVar(&dryRun, "dry-run", false, "print the plan and remove nothing")
	return command
}

// modulesIn is the list a refusal can offer when an id is not one of them.
func modulesIn(p *project.Project) string {
	ids, err := p.Modules()
	if err != nil || len(ids) == 0 {
		return ""
	}
	return " (this project has: " + strings.Join(ids, ", ") + ")"
}

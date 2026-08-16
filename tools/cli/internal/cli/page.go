package cli

import (
	"fmt"
	"os"

	"github.com/spf13/cobra"

	"github.com/SekiroKenjii/kakehashi/tools/cli/internal/generate"
	"github.com/SekiroKenjii/kakehashi/tools/cli/internal/project"
)

func addPageCommand() *cobra.Command {
	var (
		title string
		noNav bool
		dry   bool
	)

	command := &cobra.Command{
		Use:   "page <module> <PageName>",
		Short: "Generate a page inside a module that already exists",
		Long: "Generate a page inside a module that already exists.\n\n" +
			"The name is the page's own, in PascalCase and without the word Page: the generator\n" +
			"writes OrdersPage and OrdersPageViewModel from Orders. This touches the client only.",
		Args: func(_ *cobra.Command, args []string) error {
			if len(args) != 2 {
				return usagef("add page takes a module and a page name, for example " +
					"'kakehashi add page orders Backlog'")
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
			result, err := generate.AddPage(generate.PageOptions{
				Project: p,
				Module:  args[0],
				Page:    args[1],
				Title:   title,
				Nav:     !noNav,
				DryRun:  dry,
				Log:     func(format string, a ...any) { fmt.Fprintf(out, "  %s\n", fmt.Sprintf(format, a...)) },
			})
			if err != nil {
				return err
			}

			for _, file := range result.Files {
				if dry {
					fmt.Fprintf(out, "    create  %s\n", file)
					continue
				}
				fmt.Fprintf(out, "  %s\n", file)
			}
			if dry {
				for _, file := range result.Wiring {
					fmt.Fprintf(out, "    wire    %s\n", file)
				}
				fmt.Fprintf(out, "\n  dry run: nothing was written\n")
			}
			return nil
		},
	}

	flags := command.Flags()
	flags.StringVar(&title, "title", "", "what the navigation pane shows (default: the name, spaced)")
	flags.BoolVar(&noNav, "no-nav", false, "register the page without giving it a navigation entry")
	flags.BoolVar(&dry, "dry-run", false, "print the plan and write nothing")
	return command
}

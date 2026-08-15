package cli

import (
	"fmt"
	"strings"

	"github.com/spf13/cobra"

	"github.com/SekiroKenjii/kakehashi/tools/cli/internal/template"
)

func versionCommand() *cobra.Command {
	return &cobra.Command{
		Use:   "version",
		Short: "Print the CLI version and the templates in the cache",
		Args:  cobra.NoArgs,
		RunE: func(command *cobra.Command, _ []string) error {
			out := command.OutOrStdout()
			fmt.Fprintf(out, "kakehashi %s\n", version)

			client := template.New(template.Client{})
			cached, err := client.Cached()
			if err != nil {
				return err
			}
			if len(cached) == 0 {
				fmt.Fprintf(out, "templates cached: none in %s\n", client.CacheDir)
				return nil
			}
			fmt.Fprintf(out, "templates cached: %s\n", strings.Join(cached, ", "))
			return nil
		},
	}
}

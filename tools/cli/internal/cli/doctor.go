package cli

import (
	"encoding/json"
	"fmt"
	"io"
	"text/tabwriter"

	"github.com/spf13/cobra"

	"github.com/SekiroKenjii/kakehashi/tools/cli/internal/checks"
)

// report is `doctor --json`. Ok is the one field a script needs, and it is false when a required
// check failed — an advisory warning is not a reason to stop a pipeline.
type doctorReport struct {
	OK     bool            `json:"ok"`
	Checks []checks.Result `json:"checks"`
}

func doctorCommand() *cobra.Command {
	asJSON := false
	command := &cobra.Command{
		Use:   "doctor",
		Short: "Check this machine for what building a scaffolded project needs",
		Args:  cobra.NoArgs,
		RunE: func(command *cobra.Command, _ []string) error {
			results := checks.Run(command.Context(), checks.All())
			ok := len(checks.Failed(results)) == 0

			if asJSON {
				return writeJSON(command.OutOrStdout(), doctorReport{OK: ok, Checks: results})
			}
			writeTable(command.OutOrStdout(), results)
			if !ok {
				return fmt.Errorf("%d required check(s) failed", len(checks.Failed(results)))
			}
			return nil
		},
	}
	command.Flags().BoolVar(&asJSON, "json", false, "print the report as JSON")
	return command
}

func writeJSON(out io.Writer, report doctorReport) error {
	encoder := json.NewEncoder(out)
	encoder.SetIndent("", "  ")
	return encoder.Encode(report)
}

func writeTable(out io.Writer, results []checks.Result) {
	table := tabwriter.NewWriter(out, 0, 0, 2, ' ', 0)
	for _, result := range results {
		fmt.Fprintf(table, "%s\t%s\t%s\n", symbol(result.Status), result.Name, result.Detail)
	}
	table.Flush()

	for _, result := range results {
		if result.Fix != "" && result.Status != checks.Pass {
			fmt.Fprintf(out, "\n%s %s\n  %s\n", symbol(result.Status), result.Name, result.Fix)
		}
	}
}

func symbol(status checks.Status) string {
	switch status {
	case checks.Pass:
		return "OK "
	case checks.Warn:
		return "!! "
	case checks.Fail:
		return "XX "
	default:
		return "-- "
	}
}

// Command gen-sync derives the generator's templates from the example module in this repository.
//
//	cd tools/cli && go run ./cmd/gen-sync
//
// Run it after changing the example module, and commit what it writes. CI runs the derivation
// again and fails on a diff, which is what keeps the module a generator writes and the module the
// template ships from drifting apart.
package main

import (
	"fmt"
	"os"
	"os/exec"
	"strings"

	"github.com/SekiroKenjii/kakehashi/tools/cli/internal/gensync"
)

func main() {
	root, err := repository()
	if err != nil {
		fail(err)
	}

	report, err := gensync.Derive(root)
	if err != nil {
		fail(err)
	}

	fmt.Printf("gen-sync: %d templates, %d wiring sites, %d generated paths\n",
		len(report.Templates), report.Wiring, len(report.Generated))
}

func repository() (string, error) {
	out, err := exec.Command("git", "rev-parse", "--show-toplevel").Output()
	if err != nil {
		return "", fmt.Errorf("gen-sync runs inside the template repository: %w", err)
	}
	return strings.TrimSpace(string(out)), nil
}

func fail(err error) {
	fmt.Fprintln(os.Stderr, "gen-sync:", err)
	os.Exit(1)
}

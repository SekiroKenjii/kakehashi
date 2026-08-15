// Command inventory is the automated half of the Phase 0 inventory. It reports where this
// repository names itself and where the example modules reach, and it checks that
// docs/BOILERPLATE.md still classifies every tracked file.
//
// Two modes, both over `git ls-files`, so an untracked scratch file never enters the map:
//
//	cd tools/inventory && go run .             CSV on stdout: path, match, line, suggested_group
//	cd tools/inventory && go run . -coverage   fails on a file the map misses, or a row it no longer covers
//
// It lives in its own module because it belongs to the repository rather than to the server, and
// depends on nothing outside the standard library, so it runs on a clone with no network.
package main

import (
	"bufio"
	"flag"
	"fmt"
	"os"
	"os/exec"
	"strings"
)

func main() {
	checkCoverage := flag.Bool("coverage", false, "check docs/BOILERPLATE.md against the tracked file list")
	flag.Parse()

	root, err := repoRoot()
	if err != nil {
		fatal(err)
	}

	files, err := trackedFiles(root)
	if err != nil {
		fatal(err)
	}

	out := bufio.NewWriter(os.Stdout)
	defer out.Flush()

	if *checkCoverage {
		ok, err := coverage(root, files, out)
		if err != nil {
			fatal(err)
		}
		if !ok {
			out.Flush()
			os.Exit(1)
		}
		return
	}

	if err := scan(root, files, out); err != nil {
		fatal(err)
	}
}

// repoRoot resolves the working tree the tool reports on, so it runs from any directory inside it.
func repoRoot() (string, error) {
	out, err := exec.Command("git", "rev-parse", "--show-toplevel").Output()
	if err != nil {
		return "", fmt.Errorf("locate the repository: %w", err)
	}
	return strings.TrimSpace(string(out)), nil
}

// trackedFiles returns every path git tracks, relative to root and slash-separated on every
// platform. -z is what keeps a path with a space or a quote in it intact.
func trackedFiles(root string) ([]string, error) {
	out, err := exec.Command("git", "-C", root, "ls-files", "-z").Output()
	if err != nil {
		return nil, fmt.Errorf("list tracked files: %w", err)
	}

	paths := strings.Split(strings.TrimSuffix(string(out), "\x00"), "\x00")
	if len(paths) == 1 && paths[0] == "" {
		return nil, nil
	}
	return paths, nil
}

func fatal(err error) {
	fmt.Fprintln(os.Stderr, "inventory:", err)
	os.Exit(1)
}

// Command units removes a removable unit from the tree: it deletes the unit's paths and strips the
// marker regions that wire them in.
//
//	cd tools/units && go run . ../../templates/units/notes.json
//
// Run it before the rename, while the unit file and the tree still agree on placeholder paths. The
// Phase 2 CLI ports this; until then it is what proves --bare is a promise the template can keep.
package main

import (
	"encoding/json"
	"fmt"
	"os"
	"os/exec"
	"path/filepath"
	"regexp"
	"strings"
)

// unit is templates/units/*.json. Sections are documentation for the reader and for a verifier:
// removal keys off the unit's own marker, which carries the id.
type unit struct {
	SchemaVersion int    `json:"schemaVersion"`
	ID            string `json:"id"`
	Paths         []string
	Markers       []struct {
		File     string
		Sections []string
	}
}

func main() {
	if len(os.Args) != 2 {
		fatal(fmt.Errorf("usage: units <unit-file.json>"))
	}

	root, err := repoRoot()
	if err != nil {
		fatal(err)
	}

	u, err := load(os.Args[1])
	if err != nil {
		fatal(err)
	}

	for _, p := range u.Paths {
		target := filepath.Join(root, filepath.FromSlash(p))
		if _, err := os.Stat(target); err != nil {
			fatal(fmt.Errorf("unit %s: path does not exist: %s", u.ID, p))
		}
		if err := os.RemoveAll(target); err != nil {
			fatal(err)
		}
	}

	begin := regexp.MustCompile(`kakehashi:unit-` + regexp.QuoteMeta(u.ID) + `:begin`)
	end := regexp.MustCompile(`kakehashi:unit-` + regexp.QuoteMeta(u.ID) + `:end`)
	for _, m := range u.Markers {
		if err := strip(filepath.Join(root, filepath.FromSlash(m.File)), begin, end); err != nil {
			fatal(fmt.Errorf("unit %s: %w", u.ID, err))
		}
	}

	// The unit file claims to be complete, so nothing may still carry its marker.
	if left, err := survivors(root, begin, end); err != nil {
		fatal(err)
	} else if len(left) > 0 {
		fatal(fmt.Errorf("unit %s: markers left behind in %s", u.ID, strings.Join(left, ", ")))
	}

	fmt.Printf("units: removed %s — %d paths, %d files unwired\n", u.ID, len(u.Paths), len(u.Markers))
}

func load(path string) (*unit, error) {
	body, err := os.ReadFile(path)
	if err != nil {
		return nil, err
	}

	var u unit
	if err := json.Unmarshal(body, &u); err != nil {
		return nil, fmt.Errorf("parse %s: %w", path, err)
	}
	if u.SchemaVersion != 1 {
		return nil, fmt.Errorf("%s: unsupported schemaVersion %d", path, u.SchemaVersion)
	}
	if u.ID == "" {
		return nil, fmt.Errorf("%s: no id", path)
	}
	return &u, nil
}

// strip removes the lines between the unit's markers, and the markers themselves.
func strip(path string, begin, end *regexp.Regexp) error {
	body, err := os.ReadFile(path)
	if err != nil {
		return err
	}

	var kept []string
	depth := 0
	for _, line := range strings.Split(string(body), "\n") {
		switch {
		case begin.MatchString(line):
			depth++
		case end.MatchString(line):
			depth--
		case depth == 0:
			kept = append(kept, line)
		}
	}
	if depth != 0 {
		return fmt.Errorf("unbalanced markers in %s", path)
	}
	return os.WriteFile(path, []byte(strings.Join(kept, "\n")), 0o644)
}

func survivors(root string, begin, end *regexp.Regexp) ([]string, error) {
	out, err := exec.Command("git", "-C", root, "ls-files").Output()
	if err != nil {
		return nil, fmt.Errorf("list tracked files: %w", err)
	}

	var left []string
	for _, rel := range strings.Split(strings.TrimSpace(string(out)), "\n") {
		body, err := os.ReadFile(filepath.Join(root, filepath.FromSlash(rel)))
		if err != nil {
			continue
		}
		if begin.Match(body) || end.Match(body) {
			left = append(left, rel)
		}
	}
	return left, nil
}

func repoRoot() (string, error) {
	out, err := exec.Command("git", "rev-parse", "--show-toplevel").Output()
	if err != nil {
		return "", fmt.Errorf("locate the repository: %w", err)
	}
	return strings.TrimSpace(string(out)), nil
}

func fatal(err error) {
	fmt.Fprintln(os.Stderr, "units:", err)
	os.Exit(1)
}

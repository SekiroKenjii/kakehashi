package scaffold

import (
	"fmt"
	"os"
	"path"
	"regexp"
	"sort"
	"strings"

	"github.com/SekiroKenjii/kakehashi/tools/cli/internal/unitfile"
)

// prune removes the units this scaffold did not ask for and reports both lists for the manifest.
func prune(work string, opts Options) (applied, removed []string, err error) {
	all, err := units(work, opts.Descriptor)
	if err != nil {
		return nil, nil, err
	}

	wanted := map[string]bool{}
	if !opts.Inputs.WithExample {
		for _, id := range opts.Descriptor.ExampleUnits {
			wanted[id] = true
		}
	}

	known := map[string]bool{}
	for _, u := range all {
		known[u.ID] = true
	}
	for id := range wanted {
		if !known[id] {
			return nil, nil, fmt.Errorf("the template declares example unit %q but ships no unit file for it", id)
		}
	}

	applied, removed = []string{}, []string{}
	for _, u := range all {
		if !wanted[u.ID] {
			applied = append(applied, u.ID)
			continue
		}
		if err := removeUnit(work, u, opts.Descriptor.Units); err != nil {
			return nil, nil, fmt.Errorf("unit %s: %w", u.ID, err)
		}
		removed = append(removed, u.ID)
	}
	sort.Strings(applied)
	sort.Strings(removed)
	return applied, removed, nil
}

// removeUnit deletes the unit's paths, strips the marker regions that wire them in, and takes the
// unit file with it. A path the unit claims but the tree does not have means the two disagree,
// which is worth stopping for: the alternative is a project missing wiring nobody removed.
func removeUnit(work string, u *unitfile.Unit, unitsDir string) error {
	for _, path := range u.Paths {
		target, err := under(work, path)
		if err != nil {
			return err
		}
		if _, err := os.Stat(target); err != nil {
			return fmt.Errorf("path does not exist: %s", path)
		}
		if err := os.RemoveAll(target); err != nil {
			return err
		}
	}

	begin, end := u.Region()
	for _, marker := range u.Markers {
		file, err := under(work, marker.File)
		if err != nil {
			return err
		}
		if err := stripRegion(file, begin, end); err != nil {
			return err
		}
	}

	// The unit file claims to be complete, so nothing may still carry its marker.
	left, err := markerSurvivors(work, begin, end)
	if err != nil {
		return err
	}
	if len(left) > 0 {
		return fmt.Errorf("markers left behind in %s", strings.Join(left, ", "))
	}

	file, err := under(work, path.Join(unitsDir, u.ID+".json"))
	if err != nil {
		return err
	}
	return os.Remove(file)
}

// stripRegion removes the lines between the unit's markers, and the markers themselves.
func stripRegion(file string, begin, end *regexp.Regexp) error {
	body, err := os.ReadFile(file)
	if err != nil {
		return err
	}
	info, err := os.Stat(file)
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
			// An end before its begin would otherwise take everything between the two out and
			// leave a balanced count behind to say nothing happened.
			if depth < 0 {
				return fmt.Errorf("an end marker precedes its begin in %s", file)
			}
		case depth == 0:
			kept = append(kept, line)
		}
	}
	if depth != 0 {
		return fmt.Errorf("unbalanced markers in %s", file)
	}
	return os.WriteFile(file, []byte(strings.Join(kept, "\n")), info.Mode().Perm())
}

func markerSurvivors(root string, begin, end *regexp.Regexp) ([]string, error) {
	var left []string
	err := walkTextFiles(root, func(rel string, body []byte) error {
		if begin.Match(body) || end.Match(body) {
			left = append(left, rel)
		}
		return nil
	})
	sort.Strings(left)
	return left, err
}

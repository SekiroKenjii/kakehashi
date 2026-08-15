package scaffold

import (
	"fmt"
	"io/fs"
	"os"
	"path/filepath"
	"strings"

	"github.com/SekiroKenjii/kakehashi/tools/cli/internal/template"
)

// trim drops what belongs to the template repository rather than to a project made from it: whole
// paths, index entries pointing at them, and the README swap that gives the project its own.
func trim(root string, d *template.Descriptor) error {
	for _, path := range d.Exclude {
		if err := os.RemoveAll(filepath.Join(root, filepath.FromSlash(path))); err != nil {
			return err
		}
	}

	for _, exclusion := range d.ExcludeLines {
		if err := dropLines(filepath.Join(root, filepath.FromSlash(exclusion.File)), exclusion.Match); err != nil {
			return fmt.Errorf("excludeLines %s: %w", exclusion.File, err)
		}
	}

	for _, move := range d.Move {
		from := filepath.Join(root, filepath.FromSlash(move.From))
		to := filepath.Join(root, filepath.FromSlash(move.To))
		if err := os.MkdirAll(filepath.Dir(to), 0o755); err != nil {
			return err
		}
		if err := os.Rename(from, to); err != nil {
			return fmt.Errorf("move %s: %w", move.From, err)
		}
	}
	return nil
}

// removeEmptyDirs deletes the directories that trimming and unit removal emptied, deepest first so
// that a directory whose only content was another empty one goes too. A template comes out of git,
// which cannot carry an empty directory, so anything empty here was emptied by this run.
func removeEmptyDirs(root string) error {
	var dirs []string
	err := filepath.WalkDir(root, func(path string, entry fs.DirEntry, err error) error {
		if err != nil {
			return err
		}
		if entry.IsDir() && path != root {
			dirs = append(dirs, path)
		}
		return nil
	})
	if err != nil {
		return err
	}

	for i := len(dirs) - 1; i >= 0; i-- {
		entries, err := os.ReadDir(dirs[i])
		if err != nil {
			return err
		}
		if len(entries) == 0 {
			if err := os.Remove(dirs[i]); err != nil {
				return err
			}
		}
	}
	return nil
}

// dropLines deletes every line of a file that contains one of the patterns.
func dropLines(path string, patterns []string) error {
	body, err := os.ReadFile(path)
	if err != nil {
		return err
	}
	info, err := os.Stat(path)
	if err != nil {
		return err
	}

	kept := make([]string, 0, strings.Count(string(body), "\n")+1)
	for _, line := range strings.Split(string(body), "\n") {
		drop := false
		for _, pattern := range patterns {
			if strings.Contains(line, pattern) {
				drop = true
				break
			}
		}
		if !drop {
			kept = append(kept, line)
		}
	}
	return os.WriteFile(path, []byte(strings.Join(kept, "\n")), info.Mode().Perm())
}

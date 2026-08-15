package scaffold

import (
	"io/fs"
	"os"
	"path/filepath"
	"sort"
	"strings"
)

// renamePaths substitutes the placeholders in file and directory names, deepest first so that
// renaming a directory never invalidates a path still queued beneath it. Only the leaf is
// substituted: a queued path's ancestors are still spelled with placeholders until their own,
// shallower turn, so a fully substituted destination names a directory that does not exist yet.
func renamePaths(root string, in Inputs) (int, error) {
	queued, err := placeholderPaths(root)
	if err != nil {
		return 0, err
	}

	renamed := 0
	for _, path := range queued {
		leaf := filepath.Base(path)
		next := in.apply(leaf)
		if next == leaf {
			continue
		}
		if err := os.Rename(path, filepath.Join(filepath.Dir(path), next)); err != nil {
			return renamed, err
		}
		renamed++
	}
	return renamed, nil
}

// placeholderPaths lists every path whose own name carries a placeholder, deepest first.
func placeholderPaths(root string) ([]string, error) {
	var found []string
	err := filepath.WalkDir(root, func(path string, entry fs.DirEntry, err error) error {
		if err != nil {
			return err
		}
		if entry.IsDir() && entry.Name() == ".git" {
			return fs.SkipDir
		}
		if path != root && placeholderPattern.MatchString(entry.Name()) {
			found = append(found, path)
		}
		return nil
	})
	if err != nil {
		return nil, err
	}

	sort.SliceStable(found, func(i, j int) bool {
		return depth(found[i]) > depth(found[j])
	})
	return found, nil
}

func depth(path string) int { return strings.Count(filepath.ToSlash(path), "/") }

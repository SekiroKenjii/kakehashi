package scaffold

import (
	"fmt"
	"io/fs"
	"os"
	"path/filepath"
	"strings"
)

// workPrefix names the temporary directory a scaffold builds in. A scaffold whose destination sits
// inside its own source would otherwise copy its own work into itself.
const workPrefix = ".kakehashi-"

// skipped never reaches a project: a repository, build output, and buf's cache. The template
// tracks nothing under any of these names, and an extracted release archive carries none of them.
var skipped = map[string]bool{
	".git":         true,
	"obj":          true,
	"bin":          true,
	".buf-cache":   true,
	"node_modules": true,
}

// stage copies the template tree into the working directory, returning how many files it wrote.
func stage(src, dst string) (int, error) {
	source, err := filepath.Abs(src)
	if err != nil {
		return 0, err
	}
	work, err := filepath.Abs(dst)
	if err != nil {
		return 0, err
	}

	staged := 0
	err = filepath.WalkDir(source, func(path string, entry fs.DirEntry, err error) error {
		if err != nil {
			return err
		}
		if path == source {
			return nil
		}
		if path == work || strings.HasPrefix(entry.Name(), workPrefix) {
			if entry.IsDir() {
				return fs.SkipDir
			}
			return nil
		}

		rel, err := filepath.Rel(source, path)
		if err != nil {
			return err
		}
		target := filepath.Join(work, rel)

		if entry.IsDir() {
			if skipped[entry.Name()] {
				return fs.SkipDir
			}
			return os.MkdirAll(target, 0o755)
		}
		// A symlink cannot be reproduced on every machine a project is cloned to, and copying its
		// target instead would change what the file is.
		if !entry.Type().IsRegular() {
			return fmt.Errorf("%s is not a regular file", rel)
		}

		if err := copyFile(path, target); err != nil {
			return err
		}
		staged++
		return nil
	})
	return staged, err
}

func copyFile(src, dst string) error {
	body, err := os.ReadFile(src)
	if err != nil {
		return err
	}
	info, err := os.Stat(src)
	if err != nil {
		return err
	}
	if err := os.MkdirAll(filepath.Dir(dst), 0o755); err != nil {
		return err
	}
	return os.WriteFile(dst, body, info.Mode().Perm())
}

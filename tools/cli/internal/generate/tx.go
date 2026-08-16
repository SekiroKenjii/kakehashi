package generate

import (
	"errors"
	"fmt"
	"os"
	"path/filepath"
	"sort"
	"strings"
)

// tx is a set of changes to a project that can be taken back whole.
//
// A generator writes across both halves and then asks a compiler whether it was right, so the
// answer arrives after the writing. Everything it touches is recorded here: a file it created, a
// directory it had to make on the way, and the previous contents of anything it edited. A failure
// at any step puts all of it back.
type tx struct {
	root    string
	created []string
	saved   map[string]saved
}

type saved struct {
	body []byte
	mode os.FileMode
}

func newTx(root string) *tx {
	return &tx{root: root, saved: map[string]saved{}}
}

// Create writes a file the project did not have. It refuses to overwrite: every path a generator
// writes is new, and one that is not means the module is already there in some form.
func (t *tx) Create(rel, body string) error {
	path := filepath.Join(t.root, filepath.FromSlash(rel))
	if _, err := os.Stat(path); err == nil {
		return fmt.Errorf("%s already exists", rel)
	}

	if missing := topmostMissing(filepath.Dir(path)); missing != "" {
		t.created = append(t.created, missing)
	} else {
		t.created = append(t.created, path)
	}
	if err := os.MkdirAll(filepath.Dir(path), 0o755); err != nil {
		return err
	}
	return os.WriteFile(path, []byte(body), 0o644)
}

// Edit rewrites a file the project already has, remembering what it held.
func (t *tx) Edit(rel string, change func(string) (string, error)) error {
	path := filepath.Join(t.root, filepath.FromSlash(rel))
	body, err := os.ReadFile(path)
	if err != nil {
		return fmt.Errorf("%s: %w", rel, err)
	}
	info, err := os.Stat(path)
	if err != nil {
		return err
	}

	if _, remembered := t.saved[path]; !remembered {
		t.saved[path] = saved{body: body, mode: info.Mode().Perm()}
	}

	next, err := change(string(body))
	if err != nil {
		return fmt.Errorf("%s: %w", rel, err)
	}
	return os.WriteFile(path, []byte(next), info.Mode().Perm())
}

// Track records a path the transaction did not write but has to take back — the tree a code
// generator produces from a file the transaction did write.
func (t *tx) Track(rel string) {
	t.created = append(t.created, filepath.Join(t.root, filepath.FromSlash(rel)))
}

// Rollback undoes every change, and keeps going after a failure: leaving half of a rollback undone
// because the other half failed is the state this exists to prevent.
func (t *tx) Rollback() error {
	var failures []string

	for path, was := range t.saved {
		if err := os.MkdirAll(filepath.Dir(path), 0o755); err != nil {
			failures = append(failures, fmt.Sprintf("recreate %s: %v", t.rel(filepath.Dir(path)), err))
			continue
		}
		if err := os.WriteFile(path, was.body, was.mode); err != nil {
			failures = append(failures, fmt.Sprintf("restore %s: %v", t.rel(path), err))
		}
	}
	// Deepest first, so a directory is taken back after what it holds.
	paths := append([]string{}, t.created...)
	sort.Slice(paths, func(i, j int) bool { return len(paths[i]) > len(paths[j]) })
	for _, path := range paths {
		if err := os.RemoveAll(path); err != nil {
			failures = append(failures, fmt.Sprintf("remove %s: %v", t.rel(path), err))
		}
	}

	t.created, t.saved = nil, map[string]saved{}
	if len(failures) > 0 {
		return errors.New("the rollback did not finish:\n  " + strings.Join(failures, "\n  "))
	}
	return nil
}

// Touched is every path the transaction changed, project-relative, for the record it leaves.
func (t *tx) Touched() []string {
	paths := make([]string, 0, len(t.created))
	for _, path := range t.created {
		paths = append(paths, t.rel(path))
	}
	sort.Strings(paths)
	return paths
}

func (t *tx) rel(path string) string {
	out, err := filepath.Rel(t.root, path)
	if err != nil {
		return path
	}
	return filepath.ToSlash(out)
}

// topmostMissing is the highest directory that does not exist yet, which is what has to go when a
// run is taken back: removing only the leaf would leave the empty parents it needed.
func topmostMissing(dir string) string {
	missing := ""
	for at := dir; ; {
		if _, err := os.Stat(at); err == nil {
			break
		}
		missing = at

		up := filepath.Dir(at)
		if up == at {
			break
		}
		at = up
	}
	return missing
}

// Delete removes a path and remembers everything under it, so a rollback can put it back. A
// removal is checked by a compiler like a generation is, and the check comes after the deleting.
func (t *tx) Delete(rel string) error {
	path := filepath.Join(t.root, filepath.FromSlash(rel))
	info, err := os.Stat(path)
	if os.IsNotExist(err) {
		return nil
	}
	if err != nil {
		return err
	}

	if !info.IsDir() {
		if err := t.remember(path); err != nil {
			return err
		}
		return os.Remove(path)
	}

	err = filepath.WalkDir(path, func(at string, entry os.DirEntry, err error) error {
		if err != nil || entry.IsDir() {
			return err
		}
		return t.remember(at)
	})
	if err != nil {
		return err
	}
	return os.RemoveAll(path)
}

// remember saves a file's contents before it goes.
func (t *tx) remember(path string) error {
	if _, already := t.saved[path]; already {
		return nil
	}

	body, err := os.ReadFile(path)
	if err != nil {
		return err
	}
	info, err := os.Stat(path)
	if err != nil {
		return err
	}
	t.saved[path] = saved{body: body, mode: info.Mode().Perm()}
	return nil
}

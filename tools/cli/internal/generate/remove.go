package generate

import (
	"fmt"
	"os"
	"path/filepath"
	"sort"
	"strings"

	"github.com/SekiroKenjii/kakehashi/tools/cli/internal/marker"
	"github.com/SekiroKenjii/kakehashi/tools/cli/internal/project"
	"github.com/SekiroKenjii/kakehashi/tools/cli/internal/unitfile"
)

// RemoveOptions is one `remove module`.
type RemoveOptions struct {
	Project *project.Project
	ID      string
	Force   bool
	DryRun  bool
	Log     func(format string, args ...any)
}

// RemoveResult is what was taken out.
type RemoveResult struct {
	Paths  []string
	Wiring []string
	Record string
	Schema string
}

// Remove takes a module back out, reading what to remove from the record its generation left —
// or, for a module the template shipped, from the unit file the template ships.
//
// It is the same transaction as adding: the removal is checked by a compiler, and a project that
// no longer builds without the module is put back the way it was.
func Remove(opts RemoveOptions) (*RemoveResult, error) {
	if opts.Log == nil {
		opts.Log = func(string, ...any) {}
	}

	unit, err := opts.Project.Unit(opts.ID)
	if err != nil {
		return nil, err
	}
	if err := clean(opts); err != nil {
		return nil, err
	}

	result := &RemoveResult{
		Paths:  append([]string{}, unit.Paths...),
		Record: recordPath(opts.Project, unit),
		Schema: fmt.Sprintf("DROP SCHEMA %s;", opts.ID),
	}
	for _, m := range unit.Markers {
		result.Wiring = append(result.Wiring, m.File)
	}
	sort.Strings(result.Paths)

	if opts.DryRun {
		return result, nil
	}

	tx := newTx(opts.Project.Root)
	if err := take(opts, tx, unit); err != nil {
		if back := tx.Rollback(); back != nil {
			return nil, fmt.Errorf("%w\n\n%v", err, back)
		}
		opts.Log("rolled back: the module is still here")
		return nil, err
	}
	return result, nil
}

// take deletes the module's files, unpicks its wiring, and asks the compiler whether anything else
// was leaning on it.
func take(opts RemoveOptions, tx *tx, unit *unitfile.Unit) error {
	for _, path := range unit.Paths {
		if err := tx.Delete(path); err != nil {
			return err
		}
	}
	opts.Log("removed %d paths", len(unit.Paths))

	for _, m := range unit.Markers {
		if err := tx.Edit(m.File, func(body string) (string, error) {
			return marker.Strip(body, unit.ID)
		}); err != nil {
			return err
		}
	}

	// The record claims to be complete, so nothing may still carry the module's marker.
	if left, err := survivors(opts.Project, unit.ID); err != nil {
		return err
	} else if len(left) > 0 {
		return fmt.Errorf("the record is incomplete: %s still wires %s in", strings.Join(left, ", "), unit.ID)
	}
	opts.Log("unpicked the wiring in %d files", len(unit.Markers))

	if err := verifyServer(Options{Project: opts.Project, Log: opts.Log}); err != nil {
		return fmt.Errorf("%w\n\nSomething outside the module refers to it. Remove those references "+
			"first: this is the compiler naming them, and the project is back the way it was", err)
	}

	if path := recordPath(opts.Project, unit); path != "" {
		if err := tx.Delete(path); err != nil {
			return err
		}
	}
	return removeEmptyDirs(opts.Project, unit.Paths)
}

// clean refuses to work on a tree with other changes in it, because the way to check what a
// removal did is to read the diff, and a diff with somebody else's work in it says nothing.
func clean(opts RemoveOptions) error {
	if opts.Force || opts.DryRun {
		return nil
	}

	dirty, err := opts.Project.Dirty()
	if err != nil {
		return err
	}
	if len(dirty) > 0 {
		return fmt.Errorf("the working tree has %d changes in it. Commit or stash them, or pass "+
			"--force:\n  %s", len(dirty), strings.Join(dirty, "\n  "))
	}
	return nil
}

// recordPath is where the module's own unit file lives, and nothing when the template ships it: a
// removal takes back what it wrote, and the template's unit files are not its to delete.
func recordPath(p *project.Project, unit *unitfile.Unit) string {
	path := project.RecordDir + "/" + unit.ID + ".json"
	if _, err := os.Stat(p.Path(path)); err != nil {
		return ""
	}
	return path
}

func survivors(p *project.Project, id string) ([]string, error) {
	var left []string
	err := filepath.WalkDir(p.Root, func(path string, entry os.DirEntry, err error) error {
		if err != nil {
			return err
		}
		if entry.IsDir() {
			if skipScan[entry.Name()] {
				return filepath.SkipDir
			}
			return nil
		}
		if !marked[filepath.Ext(entry.Name())] {
			return nil
		}

		body, err := os.ReadFile(path)
		if err != nil {
			return err
		}
		if marker.Has(string(body), id) {
			rel, _ := filepath.Rel(p.Root, path)
			left = append(left, filepath.ToSlash(rel))
		}
		return nil
	})
	sort.Strings(left)
	return left, err
}

// skipScan is what the survivor scan does not read: a repository, build output, and the records
// themselves, which name the module on purpose.
var skipScan = map[string]bool{".git": true, "obj": true, "bin": true, ".kakehashi": true}

// marked is every file type a marker can be written in.
var marked = map[string]bool{
	".go": true, ".cs": true, ".proto": true, ".xaml": true,
	".slnx": true, ".csproj": true, ".props": true, ".xml": true,
}

// removeEmptyDirs takes back the directories the module's files were the only content of.
func removeEmptyDirs(p *project.Project, paths []string) error {
	for _, path := range paths {
		at := filepath.Dir(p.Path(path))
		for strings.HasPrefix(at, p.Root) && at != p.Root {
			entries, err := os.ReadDir(at)
			if err != nil || len(entries) > 0 {
				break
			}
			if err := os.Remove(at); err != nil {
				break
			}
			at = filepath.Dir(at)
		}
	}
	return nil
}

// Package project opens a scaffolded project: it finds the manifest, reads the identity the
// scaffold recorded there, and keeps the unit records that say what a generated module wrote.
//
// A generator running inside a project has no placeholders to read. Everything it needs to spell a
// name — the Go module path, the proto package, the C# root namespace — was decided at scaffold
// time and written to the manifest, which is why the manifest is the thing that has to be found
// first.
package project

import (
	"fmt"
	"os"
	"os/exec"
	"path/filepath"
	"strings"

	"github.com/SekiroKenjii/kakehashi/tools/cli/internal/manifest"
	"github.com/SekiroKenjii/kakehashi/tools/cli/internal/semver"
	"github.com/SekiroKenjii/kakehashi/tools/cli/internal/unitfile"
)

// RecordDir is where a generated module's unit record lives, beside the manifest. The template's
// own units stay in templates/units, and removal reads both.
const RecordDir = ".kakehashi/units"

// SupportedTemplates is the range of template versions this CLI can generate into. A project older
// than the range may not have the marker sections a generator writes to; a newer one may have
// moved them.
const SupportedTemplates = ">=0.1.0 <1.0.0"

// Project is an opened project.
type Project struct {
	Root     string
	Manifest *manifest.Manifest
}

// Open finds the project that contains dir and checks this CLI can generate into it.
func Open(dir string) (*Project, error) {
	root, err := find(dir)
	if err != nil {
		return nil, err
	}

	m, err := manifest.Load(filepath.Join(root, manifest.Name))
	if err != nil {
		return nil, err
	}
	if err := supported(m.Template.Version); err != nil {
		return nil, err
	}
	return &Project{Root: root, Manifest: m}, nil
}

// find walks up from dir looking for the manifest, so a generator works anywhere in the tree.
func find(dir string) (string, error) {
	at, err := filepath.Abs(dir)
	if err != nil {
		return "", err
	}

	for {
		if _, err := os.Stat(filepath.Join(at, manifest.Name)); err == nil {
			return at, nil
		}
		up := filepath.Dir(at)
		if up == at {
			return "", fmt.Errorf("no %s here or above: run this inside a project kakehashi made", manifest.Name)
		}
		at = up
	}
}

func supported(version string) error {
	allowed, err := semver.ParseRange(SupportedTemplates)
	if err != nil {
		return err
	}
	have, err := semver.Parse(version)
	if err != nil {
		return fmt.Errorf("%s records template version %q: %w", manifest.Name, version, err)
	}
	if !allowed.Allows(have) {
		return fmt.Errorf("this project is on template %s, and this kakehashi generates into %s",
			version, SupportedTemplates)
	}
	return nil
}

// Path joins a project-relative path onto the root.
func (p *Project) Path(rel string) string {
	return filepath.Join(p.Root, filepath.FromSlash(rel))
}

// Unit is the record of what a module wrote: the one a generator left behind, or the template's
// own for a module that came with it.
func (p *Project) Unit(id string) (*unitfile.Unit, error) {
	for _, dir := range []string{RecordDir, "templates/units"} {
		path := p.Path(dir + "/" + id + ".json")
		if _, err := os.Stat(path); err == nil {
			return unitfile.Load(path)
		}
	}
	return nil, fmt.Errorf("no record of module %q: %s/%s.json does not exist, and the template "+
		"does not ship a unit file for it", id, RecordDir, id)
}

// WriteUnit records what a generated module wrote, in the same format the template uses for its
// own removable units. Removal reads this and nothing else, so a module that is not in it is a
// module nobody can take back out.
func (p *Project) WriteUnit(u *unitfile.Unit) error {
	dir := p.Path(RecordDir)
	if err := os.MkdirAll(dir, 0o755); err != nil {
		return err
	}
	return u.Write(filepath.Join(dir, u.ID+".json"))
}

// Modules lists the module ids the project has a record for, generated or template-shipped.
func (p *Project) Modules() ([]string, error) {
	seen := map[string]bool{}
	for _, dir := range []string{RecordDir, "templates/units"} {
		units, err := unitfile.LoadDir(p.Path(dir))
		if err != nil {
			return nil, err
		}
		for _, u := range units {
			seen[u.ID] = true
		}
	}

	ids := make([]string, 0, len(seen))
	for id := range seen {
		ids = append(ids, id)
	}
	return ids, nil
}

// Dirty reports the paths git sees as changed. A generator that fails half-way is easier to undo
// when there was nothing else to undo, and removal wants the same guarantee in reverse.
func (p *Project) Dirty() ([]string, error) {
	out, err := exec.Command("git", "-C", p.Root, "status", "--porcelain").Output()
	if err != nil {
		// Not a repository, or no git: there is nothing to be dirty relative to.
		return nil, nil
	}

	var paths []string
	for _, line := range strings.Split(strings.TrimSpace(string(out)), "\n") {
		if line != "" {
			paths = append(paths, strings.TrimSpace(line))
		}
	}
	return paths, nil
}

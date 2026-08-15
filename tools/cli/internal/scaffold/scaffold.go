// Package scaffold turns a template tree into a project: it drops what belongs to the template,
// removes the units the caller did not ask for, substitutes every placeholder in content and in
// path names, and refuses to finish while a placeholder or an identity string survives.
//
// Every step runs in a temporary directory beside the destination, and the destination appears in
// one rename at the end. A failure part-way through leaves nothing behind.
package scaffold

import (
	"fmt"
	"os"
	"os/exec"
	"path/filepath"
	"strings"
	"time"

	"github.com/SekiroKenjii/kakehashi/tools/cli/internal/manifest"
	"github.com/SekiroKenjii/kakehashi/tools/cli/internal/template"
	"github.com/SekiroKenjii/kakehashi/tools/cli/internal/unitfile"
)

// Options is one scaffold. Source is a template tree on disk — an extracted release or a checkout.
type Options struct {
	Source     string
	Dest       string
	Descriptor *template.Descriptor
	Inputs     Inputs
	Origin     string
	Version    string
	CLIVersion string
	DryRun     bool
	Log        func(format string, args ...any)
}

// Result is what the scaffold did, for the caller to print.
type Result struct {
	Dest         string
	Staged       int
	Substituted  int
	Renamed      int
	UnitsApplied []string
	UnitsRemoved []string
	Generated    bool
	Formatted    bool
	Committed    bool
}

// Run scaffolds the project. On a dry run every step happens except the ones that would be visible
// afterwards: the destination is never created and no repository is initialised.
func Run(opts Options) (*Result, error) {
	if opts.Log == nil {
		opts.Log = func(string, ...any) {}
	}
	if err := opts.Inputs.Validate(); err != nil {
		return nil, err
	}
	if err := CheckDestination(opts.Dest); err != nil {
		return nil, err
	}

	parent := filepath.Dir(opts.Dest)
	if err := os.MkdirAll(parent, 0o755); err != nil {
		return nil, err
	}
	// Beside the destination rather than in the system temp directory: the last step is a rename,
	// and a rename across filesystems fails.
	work, err := os.MkdirTemp(parent, ".kakehashi-")
	if err != nil {
		return nil, err
	}
	defer os.RemoveAll(work)

	result, err := build(work, opts)
	if err != nil {
		return nil, err
	}
	if opts.DryRun {
		return result, nil
	}

	// A directory created since the check would be silently absorbed by rename on Linux and would
	// fail the scaffold on Windows. Removing it only succeeds while it is still empty.
	if _, err := os.Stat(opts.Dest); err == nil {
		if err := os.Remove(opts.Dest); err != nil {
			return nil, fmt.Errorf("%s appeared while scaffolding: %w", opts.Dest, err)
		}
	}
	if err := os.Rename(work, opts.Dest); err != nil {
		return nil, err
	}
	return result, nil
}

// build is the pipeline, in the order of docs/pivot/03-PHASE-2-CLI.md §2. Unit removal precedes
// substitution because a unit file names paths as the template spells them, with placeholders.
func build(work string, opts Options) (*Result, error) {
	result := &Result{Dest: opts.Dest}

	staged, err := stage(opts.Source, work)
	if err != nil {
		return nil, err
	}
	result.Staged = staged

	if err := trim(work, opts.Descriptor); err != nil {
		return nil, err
	}

	applied, removed, err := prune(work, opts)
	if err != nil {
		return nil, err
	}
	result.UnitsApplied, result.UnitsRemoved = applied, removed

	if err := removeEmptyDirs(work); err != nil {
		return nil, err
	}
	if err := setAuthMode(work, opts.Descriptor, opts.Inputs); err != nil {
		return nil, err
	}

	substituted, err := substitute(work, opts.Inputs)
	if err != nil {
		return nil, err
	}
	result.Substituted = substituted

	renamed, err := renamePaths(work, opts.Inputs)
	if err != nil {
		return nil, err
	}
	result.Renamed = renamed

	if result.Generated, err = regenerate(work, opts.Log); err != nil {
		return nil, err
	}
	result.Formatted = reformat(work, opts.Log)

	if err := selfCheck(work, opts.Inputs); err != nil {
		return nil, err
	}
	if err := writeManifest(work, opts, result); err != nil {
		return nil, err
	}
	if !opts.DryRun {
		result.Committed = initRepository(work, opts.Version, opts.Log)
	}
	return result, nil
}

// CheckDestination refuses anything that is not an absent path or an empty directory. Run runs it
// too; it is exported so that a caller can refuse before spending a download on a scaffold that
// has nowhere to go.
func CheckDestination(dest string) error {
	info, err := os.Stat(dest)
	if os.IsNotExist(err) {
		return nil
	}
	if err != nil {
		return err
	}
	if !info.IsDir() {
		return fmt.Errorf("%s exists and is not a directory", dest)
	}

	entries, err := os.ReadDir(dest)
	if err != nil {
		return err
	}
	if len(entries) > 0 {
		return fmt.Errorf("%s is not empty", dest)
	}
	return nil
}

func writeManifest(work string, opts Options, result *Result) error {
	m := &manifest.Manifest{
		Template:  manifest.Template{Source: opts.Origin, Version: opts.Version},
		CLI:       manifest.CLI{Version: opts.CLIVersion},
		CreatedAt: time.Now().UTC().Truncate(time.Second),
		Inputs: manifest.Inputs{
			AppName:       opts.Inputs.AppName,
			AppTitle:      opts.Inputs.AppTitle,
			RootNamespace: opts.Inputs.RootNamespace,
			GoModule:      opts.Inputs.GoModule,
			ProtoPackage:  opts.Inputs.ProtoPackage,
			Accent:        opts.Inputs.Accent,
			Author:        opts.Inputs.Author,
			Year:          opts.Inputs.Year,
			Auth:          opts.Inputs.Auth,
			WithExample:   opts.Inputs.WithExample,
		},
		Units: manifest.Units{Applied: result.UnitsApplied, Removed: result.UnitsRemoved},
	}
	return m.Write(filepath.Join(work, manifest.Name))
}

// units reads the template's unit files out of the staged tree, where trim has already had its say
// about which of them ship.
func units(work string, d *template.Descriptor) ([]*unitfile.Unit, error) {
	return unitfile.LoadDir(filepath.Join(work, filepath.FromSlash(d.Units)))
}

// run executes a command in a directory and returns its combined output, which is the only useful
// thing to say about a build tool that failed.
func run(dir, name string, args ...string) (string, error) {
	cmd := exec.Command(name, args...)
	cmd.Dir = dir
	out, err := cmd.CombinedOutput()
	if err != nil {
		return strings.TrimSpace(string(out)), fmt.Errorf("%s %s: %w", name, strings.Join(args, " "), err)
	}
	return strings.TrimSpace(string(out)), nil
}

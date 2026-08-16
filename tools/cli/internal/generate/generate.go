// Package generate writes a module into a project and takes it back out again.
//
// The files it writes come from the templates derived out of the example module; what is here is
// the order they go in, the wiring that mounts them, and the transaction that makes the whole of it
// one step. A generator that writes across two halves and then asks a compiler whether it was right
// has to be able to undo itself, because the answer arrives after the writing.
package generate

import (
	"fmt"
	"os"
	"path/filepath"
	"sort"
	"strings"

	"github.com/SekiroKenjii/kakehashi/tools/cli/internal/gen"
	"github.com/SekiroKenjii/kakehashi/tools/cli/internal/gensync"
	"github.com/SekiroKenjii/kakehashi/tools/cli/internal/marker"
	"github.com/SekiroKenjii/kakehashi/tools/cli/internal/naming"
	"github.com/SekiroKenjii/kakehashi/tools/cli/internal/project"
	"github.com/SekiroKenjii/kakehashi/tools/cli/internal/unitfile"
)

// Options is one `add module`.
type Options struct {
	Project *project.Project
	Names   naming.Names
	Client  bool
	DryRun  bool
	Log     func(format string, args ...any)
}

// Result is what was written, for the caller to print and for a test to assert on.
type Result struct {
	Files    []string
	Wiring   []string
	Record   string
	Verified []string
	Skipped  []string
}

// Add generates a module and leaves the project building, or leaves the project exactly as it was.
func Add(opts Options) (*Result, error) {
	if opts.Log == nil {
		opts.Log = func(string, ...any) {}
	}

	module, err := gen.Render(data(opts.Project, opts.Names))
	if err != nil {
		return nil, err
	}
	if err := vacant(opts.Project, opts.Names.ID, module); err != nil {
		return nil, err
	}

	files, wiring := partition(module, opts.Client)
	result := &Result{}
	for _, file := range files {
		result.Files = append(result.Files, file.Path)
	}
	for _, site := range wiring {
		result.Wiring = append(result.Wiring, site.File+" ("+site.Section+")")
	}
	result.Record = project.RecordDir + "/" + opts.Names.ID + ".json"
	sort.Strings(result.Files)

	if opts.DryRun {
		return result, nil
	}

	tx := newTx(opts.Project.Root)
	if err := pipeline(opts, tx, module, files, wiring, result); err != nil {
		if back := tx.Rollback(); back != nil {
			return nil, fmt.Errorf("%w\n\n%v", err, back)
		}
		opts.Log("rolled back: the project is as it was")
		return nil, err
	}
	return result, nil
}

// pipeline is the sequence of docs/pivot/04-PHASE-3-GENERATORS.md §1.4. Each half is written and then
// checked, so a failure names the half that caused it rather than the one that noticed.
func pipeline(opts Options, tx *tx, module *gen.Module, files []gen.File, wiring []gensync.Site, result *Result) error {
	half := func(prefix string) []gen.File {
		var out []gen.File
		for _, file := range files {
			if strings.HasPrefix(file.Path, prefix) {
				out = append(out, file)
			}
		}
		return out
	}

	// The contract first: the server's wire layer imports what the code generator writes from it,
	// so nothing on either side compiles until this has run.
	if err := write(tx, half("proto/")); err != nil {
		return err
	}
	if err := verifyContract(opts, tx, module); err != nil {
		return err
	}
	result.Verified = append(result.Verified, "buf lint", "buf generate")

	if err := write(tx, half("server/")); err != nil {
		return err
	}
	if err := insert(tx, opts.Names.ID, sites(wiring, "server/")); err != nil {
		return err
	}
	if err := verifyServer(opts); err != nil {
		return err
	}
	result.Verified = append(result.Verified, "go build", "go vet", "archlint")

	if opts.Client {
		if err := write(tx, half("client/")); err != nil {
			return err
		}
		if err := insert(tx, opts.Names.ID, sites(wiring, "client/")); err != nil {
			return err
		}

		verified, skipped, err := verifyClient(opts)
		if err != nil {
			return err
		}
		result.Verified = append(result.Verified, verified...)
		result.Skipped = append(result.Skipped, skipped...)
	}

	return record(tx, opts, module, wiring)
}

func write(tx *tx, files []gen.File) error {
	for _, file := range files {
		if err := tx.Create(file.Path, file.Body); err != nil {
			return err
		}
	}
	return nil
}

// insert carves the module's lines into the files that know every module.
func insert(tx *tx, id string, wiring []gensync.Site) error {
	for _, site := range wiring {
		style, err := marker.StyleFor(site.File)
		if err != nil {
			return err
		}
		if err := tx.Edit(site.File, func(body string) (string, error) {
			return marker.Insert(body, site.Section, id, site.Lines, site.Sorted, style)
		}); err != nil {
			return err
		}
	}
	return nil
}

// record writes what removal will read. It is written last because it describes everything before
// it, and it names the generated tree as well: buf writes that, and nothing else would take it back.
func record(tx *tx, opts Options, module *gen.Module, wiring []gensync.Site) error {
	// The module's own paths rather than the files this run wrote: a page added later lands inside
	// one of these directories, and removal has to take it with the module.
	paths := append([]string{}, module.Paths...)
	if !opts.Client {
		paths = kept(paths, "client/")
	}
	sort.Strings(paths)

	byFile := map[string][]string{}
	var order []string
	for _, site := range wiring {
		if _, seen := byFile[site.File]; !seen {
			order = append(order, site.File)
		}
		byFile[site.File] = append(byFile[site.File], site.Section)
	}

	unit := &unitfile.Unit{
		ID:          opts.Names.ID,
		Title:       opts.Names.Module + " — generated by kakehashi",
		Description: "Everything `kakehashi add module " + opts.Names.ID + "` wrote, and the wiring it carved.",
		Paths:       paths,
	}
	for _, file := range order {
		unit.Markers = append(unit.Markers, unitfile.Marker{File: file, Sections: byFile[file]})
	}

	dir := opts.Project.Path(project.RecordDir)
	if missing := topmostMissing(dir); missing != "" {
		tx.Track(strings.TrimPrefix(missing, opts.Project.Root+string(filepath.Separator)))
	} else {
		tx.Track(project.RecordDir + "/" + unit.ID + ".json")
	}
	return opts.Project.WriteUnit(unit)
}

// vacant refuses a module the project already has, in any of the three ways it can have one.
func vacant(p *project.Project, id string, module *gen.Module) error {
	if _, err := p.Unit(id); err == nil {
		return fmt.Errorf("this project already has a module called %q", id)
	}
	for _, file := range module.Files {
		if _, err := os.Stat(p.Path(file.Path)); err == nil {
			return fmt.Errorf("%s already exists, so %q is here in some form", file.Path, id)
		}
	}
	for _, site := range module.Wiring {
		body, err := os.ReadFile(p.Path(site.File))
		if err != nil {
			return fmt.Errorf("%s is a file every module is wired into, and it is not there: %w", site.File, err)
		}
		if marker.Has(string(body), id) {
			return fmt.Errorf("%s already wires %q in", site.File, id)
		}
	}
	return nil
}

// kept drops the paths under a prefix, for a generation that leaves out a half.
func kept(paths []string, prefix string) []string {
	out := make([]string, 0, len(paths))
	for _, path := range paths {
		if !strings.HasPrefix(path, prefix) {
			out = append(out, path)
		}
	}
	return out
}

func partition(module *gen.Module, client bool) ([]gen.File, []gensync.Site) {
	if client {
		return module.Files, module.Wiring
	}

	var files []gen.File
	for _, file := range module.Files {
		if !strings.HasPrefix(file.Path, "client/") {
			files = append(files, file)
		}
	}
	var wiring []gensync.Site
	for _, site := range module.Wiring {
		if !strings.HasPrefix(site.File, "client/") {
			wiring = append(wiring, site)
		}
	}
	return files, wiring
}

func sites(wiring []gensync.Site, prefix string) []gensync.Site {
	var out []gensync.Site
	for _, site := range wiring {
		if strings.HasPrefix(site.File, prefix) {
			out = append(out, site)
		}
	}
	return out
}

// data is the project's identity, as the scaffold recorded it, plus the module's own vocabulary.
func data(p *project.Project, names naming.Names) gen.Data {
	in := p.Manifest.Inputs
	return gen.Data{
		ID:            names.ID,
		Module:        names.Module,
		Entity:        names.Entity,
		Variable:      names.Variable,
		Icon:          names.Icon,
		Glyph:         names.Glyph,
		Title:         names.Title,
		AppName:       in.AppName,
		AppNameLower:  strings.ToLower(in.AppName),
		AppNameUpper:  strings.ToUpper(in.AppName),
		AppTitle:      in.AppTitle,
		RootNamespace: in.RootNamespace,
		ProtoPackage:  in.ProtoPackage,
		GoModule:      in.GoModule,
		Accent:        in.Accent,
		Author:        in.Author,
		Year:          in.Year,
	}
}

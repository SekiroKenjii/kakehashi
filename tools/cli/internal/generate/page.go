package generate

import (
	"fmt"
	"os"
	"regexp"
	"sort"
	"strings"

	"github.com/SekiroKenjii/kakehashi/tools/cli/internal/gen"
	"github.com/SekiroKenjii/kakehashi/tools/cli/internal/marker"
	"github.com/SekiroKenjii/kakehashi/tools/cli/internal/naming"
	"github.com/SekiroKenjii/kakehashi/tools/cli/internal/project"
)

// PageOptions is one `add page`.
type PageOptions struct {
	Project *project.Project
	Module  string
	Page    string
	Title   string
	Nav     bool
	DryRun  bool
	Log     func(format string, args ...any)
}

// PageResult is what was written.
type PageResult struct {
	Files  []string
	Wiring []string
}

// PageName is what a page may be called: PascalCase, and its own name rather than its suffix — the
// generator adds Page and PageViewModel to it.
var PageName = regexp.MustCompile(`^[A-Z][A-Za-z0-9]{1,39}$`)

// AddPage writes a page into a module that already exists. It touches the client and nothing else:
// a page is a screen, and the module it belongs to is already mounted on both halves.
func AddPage(opts PageOptions) (*PageResult, error) {
	if opts.Log == nil {
		opts.Log = func(string, ...any) {}
	}
	if !PageName.MatchString(opts.Page) {
		return nil, fmt.Errorf("a page name must match %s, got %q", PageName, opts.Page)
	}
	if strings.HasSuffix(opts.Page, "Page") {
		return nil, fmt.Errorf("leave the Page off %q: the generator adds it", opts.Page)
	}

	names, err := naming.New(opts.Module, "", "")
	if err != nil {
		return nil, err
	}
	if _, err := opts.Project.Unit(names.ID); err != nil {
		return nil, fmt.Errorf("%w%s", err, modules(opts.Project))
	}

	title := opts.Title
	if title == "" {
		title = spaced(opts.Page)
	}

	page, err := gen.RenderPage(gen.PageData{
		Page:          opts.Page,
		PageTitle:     title,
		Module:        names.Module,
		ID:            names.ID,
		Glyph:         names.Glyph,
		AppName:       opts.Project.Manifest.Inputs.AppName,
		RootNamespace: opts.Project.Manifest.Inputs.RootNamespace,
	})
	if err != nil {
		return nil, err
	}

	// Every path is inside the module's own UI project, which is where a page lives.
	ui := fmt.Sprintf("client/src/Modules/%s/%s.Modules.%s.UI",
		names.Module, opts.Project.Manifest.Inputs.AppName, names.Module)
	if _, err := os.Stat(opts.Project.Path(ui)); err != nil {
		return nil, fmt.Errorf("%s has no UI project at %s", names.Module, ui)
	}

	registration := fmt.Sprintf("%s/%sModule.cs", ui, names.Module)
	result := &PageResult{Wiring: []string{registration}}
	for i, file := range page.Files {
		page.Files[i].Path = ui + "/" + file.Path
		result.Files = append(result.Files, page.Files[i].Path)
	}
	sort.Strings(result.Files)

	if opts.DryRun {
		return result, nil
	}

	tx := newTx(opts.Project.Root)
	if err := writePage(opts, tx, page, registration); err != nil {
		if back := tx.Rollback(); back != nil {
			return nil, fmt.Errorf("%w\n\n%v", err, back)
		}
		opts.Log("rolled back: the project is as it was")
		return nil, err
	}
	return result, nil
}

func writePage(opts PageOptions, tx *tx, page *gen.Page, registration string) error {
	if err := write(tx, page.Files); err != nil {
		return err
	}

	// The page's own fence, so that what registered it can be found again.
	unit := "page-" + strings.ToLower(opts.Page)
	style, err := marker.StyleFor(registration)
	if err != nil {
		return err
	}

	sections := []struct {
		section string
		lines   []string
	}{
		{gen.SectionPageServices, page.Services},
	}
	if opts.Nav {
		sections = append(sections, struct {
			section string
			lines   []string
		}{gen.SectionPageNavigation, page.Navigation})
	}

	for _, at := range sections {
		if err := tx.Edit(registration, func(body string) (string, error) {
			return marker.Insert(body, at.section, unit, at.lines, false, style)
		}); err != nil {
			return err
		}
	}
	opts.Log("registered %sPage in %s", opts.Page, registration)

	verified, skipped, err := verifyClient(Options{Project: opts.Project, Log: opts.Log})
	if err != nil {
		return err
	}
	if len(verified) > 0 {
		opts.Log("verified: %s", strings.Join(verified, ", "))
	}
	if len(skipped) > 0 {
		opts.Log("not verified here: %s", strings.Join(skipped, ", "))
	}
	return nil
}

// modules is the list a refusal can offer when an id names no module.
func modules(p *project.Project) string {
	ids, err := p.Modules()
	if err != nil || len(ids) == 0 {
		return ""
	}
	sort.Strings(ids)
	return " (this project has: " + strings.Join(ids, ", ") + ")"
}

// spaced turns a PascalCase name into what a navigation pane should show.
func spaced(name string) string {
	var out strings.Builder
	for i, r := range name {
		if i > 0 && r >= 'A' && r <= 'Z' {
			out.WriteByte(' ')
		}
		out.WriteRune(r)
	}
	return out.String()
}

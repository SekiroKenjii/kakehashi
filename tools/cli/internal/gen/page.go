package gen

import (
	"embed"
	"fmt"
	"io/fs"
	"strings"
	"text/template"
)

//go:embed all:pages
var pages embed.FS

// The sections a page registers itself in, inside its module's own composition entry point. They
// are the module's rather than the application's: a page belongs to one module, and nothing outside
// it needs to know the page exists.
const (
	SectionPageServices   = "module-page-services"
	SectionPageNavigation = "module-page-navigation"
)

// PageData is what a page template may name. It is the module's vocabulary plus the page's own,
// because a page is written into a module that already exists.
type PageData struct {
	Page      string
	PageTitle string
	Module    string
	ID        string
	Glyph     string

	AppName       string
	RootNamespace string
}

// Page is a rendered page: the files, and the two lines that register it.
type Page struct {
	Files      []File
	Services   []string
	Navigation []string
}

// RenderPage produces a page inside an existing module.
//
// These templates are the one set not derived from the example module, and the reason is that the
// example has no second page to derive from: its page is the module's page, tied to the module's
// gateway and its commands. What a new page starts as is a title and somewhere to put the rest.
func RenderPage(d PageData) (*Page, error) {
	page := &Page{}

	err := fs.WalkDir(pages, "pages", func(path string, entry fs.DirEntry, err error) error {
		if err != nil || entry.IsDir() {
			return err
		}

		body, err := fs.ReadFile(pages, path)
		if err != nil {
			return err
		}

		name := strings.TrimSuffix(strings.TrimPrefix(path, "pages/"), suffix)
		target, err := textPage(name, name, d)
		if err != nil {
			return err
		}
		content, err := textPage(target, string(body), d)
		if err != nil {
			return err
		}
		page.Files = append(page.Files, File{Path: target, Body: content})
		return nil
	})
	if err != nil {
		return nil, err
	}

	page.Services = []string{
		"services.AddTransient<" + d.Page + "PageViewModel>();",
		"services.AddTransient<" + d.Page + "Page>();",
	}
	page.Navigation = []string{
		`new NavigationItem("` + d.PageTitle + `", "` + d.Glyph + `", typeof(` + d.Page + `Page)) ` +
			`{ Id = "` + strings.ToLower(d.Page) + `", Group = "Utilities" },`,
	}
	return page, nil
}

// textPage renders one template against a page's data.
func textPage(name, body string, d PageData) (string, error) {
	parsed, err := template.New(name).Option("missingkey=error").Parse(body)
	if err != nil {
		return "", fmt.Errorf("parse %s: %w", name, err)
	}

	out := &strings.Builder{}
	if err := parsed.Execute(out, d); err != nil {
		return "", fmt.Errorf("render %s: %w", name, err)
	}
	return out.String(), nil
}

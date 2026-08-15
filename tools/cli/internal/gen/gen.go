// Package gen renders a module from the templates derived out of the example module.
//
// Nothing here is written by hand: tools/cli/cmd/gen-sync produces templates/ and plan.json from
// the example module in the template repository, and a test asserts that rendering them with the
// example's own names reproduces it. What a generator writes is therefore what the template ships,
// by construction rather than by discipline.
package gen

import (
	"embed"
	"encoding/json"
	"fmt"
	"io/fs"
	"strings"
	"text/template"

	"github.com/SekiroKenjii/kakehashi/tools/cli/internal/gensync"
)

//go:embed all:templates plan.json
var files embed.FS

// root is where the templates sit inside the embedded filesystem, and suffix is what marks one.
// The suffix is not decoration: without it the Go tool would try to compile a directory of Go
// templates whose contents are not Go.
const (
	root   = "templates"
	suffix = ".tmpl"
)

// Data is everything a template may name: the module's own vocabulary, and the identity the
// project was scaffolded with.
type Data struct {
	ID       string
	Module   string
	Entity   string
	Variable string
	Icon     string
	Glyph    string
	Title    string

	AppName       string
	AppNameLower  string
	AppNameUpper  string
	AppTitle      string
	RootNamespace string
	ProtoPackage  string
	GoModule      string
	Accent        string
	Author        string
	Year          string
}

// File is one rendered file: where it goes in the project, and what goes in it.
type File struct {
	Path string
	Body string
}

// Module is a rendered module, ready to be written.
type Module struct {
	Files     []File
	Wiring    []gensync.Site
	Paths     []string
	Generated []string
}

var funcs = template.FuncMap{
	"upper":        strings.ToUpper,
	"article":      article,
	"articleUpper": func(word string) string { return strings.ToUpper(article(word)[:1]) + article(word)[1:] },
}

// article picks the one English puts in front of a word. Generated prose that reads "a order" is
// the kind of detail that tells a reader the code was not written for them.
func article(word string) string {
	if word == "" {
		return "a"
	}
	switch strings.ToLower(word[:1]) {
	case "a", "e", "i", "o", "u":
		return "an"
	default:
		return "a"
	}
}

// Render produces every file of a module, the lines it contributes to the files that wire modules
// in, and the paths the code generator will fill.
func Render(d Data) (*Module, error) {
	plan, err := loadPlan()
	if err != nil {
		return nil, err
	}

	module := &Module{}
	err = fs.WalkDir(files, root, func(path string, entry fs.DirEntry, err error) error {
		if err != nil || entry.IsDir() {
			return err
		}

		body, err := fs.ReadFile(files, path)
		if err != nil {
			return err
		}
		rendered, err := render(strings.TrimSuffix(strings.TrimPrefix(path, root+"/"), suffix), string(body), d)
		if err != nil {
			return err
		}
		module.Files = append(module.Files, *rendered)
		return nil
	})
	if err != nil {
		return nil, err
	}

	for _, site := range plan.Wiring {
		next := gensync.Site{Section: site.Section, Sorted: site.Sorted}
		if next.File, err = text("wiring path", site.File, d); err != nil {
			return nil, err
		}
		for _, line := range site.Lines {
			out, err := text("wiring line", line, d)
			if err != nil {
				return nil, err
			}
			next.Lines = append(next.Lines, out)
		}
		module.Wiring = append(module.Wiring, next)
	}

	for _, list := range []struct {
		from []string
		to   *[]string
	}{{plan.Paths, &module.Paths}, {plan.Generated, &module.Generated}} {
		for _, path := range list.from {
			out, err := text("path", path, d)
			if err != nil {
				return nil, err
			}
			*list.to = append(*list.to, out)
		}
	}
	return module, nil
}

// render turns one template into one file. The path is a template too, which is what lets the
// template tree mirror the layout it produces.
func render(pathTemplate, body string, d Data) (*File, error) {
	path, err := text(pathTemplate, pathTemplate, d)
	if err != nil {
		return nil, err
	}

	content, err := text(path, body, d)
	if err != nil {
		return nil, err
	}
	return &File{Path: path, Body: content}, nil
}

func text(name, body string, d Data) (string, error) {
	parsed, err := template.New(name).Funcs(funcs).Option("missingkey=error").Parse(body)
	if err != nil {
		return "", fmt.Errorf("parse %s: %w", name, err)
	}

	out := &strings.Builder{}
	if err := parsed.Execute(out, d); err != nil {
		return "", fmt.Errorf("render %s: %w", name, err)
	}
	return out.String(), nil
}

func loadPlan() (*gensync.Plan, error) {
	body, err := files.ReadFile("plan.json")
	if err != nil {
		return nil, err
	}

	var plan gensync.Plan
	if err := json.Unmarshal(body, &plan); err != nil {
		return nil, fmt.Errorf("parse plan.json: %w", err)
	}
	return &plan, nil
}

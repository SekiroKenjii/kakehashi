// Package gensync derives the generator's templates from the example module in the template
// repository.
//
// The module a generator writes and the module the template ships have to stay the same shape, and
// the only way to keep two copies of anything in agreement is to have one of them. The example
// module is the one: this reads it, replaces the names with template variables, and writes what
// `kakehashi add module` renders. A test asserts the round trip, so a change to the example that
// nobody re-derived fails CI rather than drifting.
package gensync

import (
	"fmt"
	"os"
	"path/filepath"
	"sort"
	"strings"
	"text/template"

	"github.com/SekiroKenjii/kakehashi/tools/cli/internal/unitfile"
)

// The example the generator is derived from, and where the derivation lands.
const (
	ExampleUnit = "templates/units/notes.json"
	TemplateDir = "tools/cli/internal/gen/templates"
	PlanFile    = "tools/cli/internal/gen/plan.json"
)

// Report is what a derivation did, for the command to print and for a test to assert on.
type Report struct {
	Templates []string
	Wiring    int
	Generated []string
}

// Derive reads the example module out of root and rewrites the generator's templates and plan.
func Derive(root string) (Report, error) {
	unit, err := unitfile.Load(filepath.Join(root, filepath.FromSlash(ExampleUnit)))
	if err != nil {
		return Report{}, fmt.Errorf("the example unit file is what says which files the module is: %w", err)
	}

	plan := Plan{}
	report := Report{}

	// The unit file lists the module's paths, so the generator's file set and the unit file the
	// scaffold ships cannot come to disagree about what the module is.
	for _, path := range unit.Paths {
		plan.Paths = append(plan.Paths, tokenise(path))
		if generated(path) {
			plan.Generated = append(plan.Generated, tokenise(path))
			report.Generated = append(report.Generated, tokenise(path))
			continue
		}

		written, err := deriveTree(root, path)
		if err != nil {
			return Report{}, err
		}
		report.Templates = append(report.Templates, written...)
	}

	// The wiring is the other half: the lines the example carved into the files that know every
	// module, read back out of the markers that fence them.
	for _, m := range unit.Markers {
		sites, err := deriveWiring(root, m.File, unit.ID)
		if err != nil {
			return Report{}, err
		}
		plan.Wiring = append(plan.Wiring, sites...)
	}
	report.Wiring = len(plan.Wiring)

	sort.Strings(report.Templates)
	if err := writePlan(root, plan); err != nil {
		return Report{}, err
	}
	return report, nil
}

// generated marks the tree buf writes. It is not a template — `buf generate` produces it from the
// proto — but removal still has to know the path.
func generated(path string) bool {
	return strings.HasPrefix(path, "server/internal/gen/")
}

// deriveTree writes one source path, file or directory, into the template tree.
func deriveTree(root, rel string) ([]string, error) {
	source := filepath.Join(root, filepath.FromSlash(rel))
	info, err := os.Stat(source)
	if err != nil {
		return nil, fmt.Errorf("the example unit names %s, which is not there: %w", rel, err)
	}
	if !info.IsDir() {
		name, err := deriveFile(root, rel)
		return []string{name}, err
	}

	var written []string
	err = filepath.WalkDir(source, func(path string, entry os.DirEntry, err error) error {
		if err != nil || entry.IsDir() {
			return err
		}

		from, err := filepath.Rel(root, path)
		if err != nil {
			return err
		}
		name, err := deriveFile(root, filepath.ToSlash(from))
		if err != nil {
			return err
		}
		written = append(written, name)
		return nil
	})
	return written, err
}

// deriveFile turns one file of the example into a template, path and content alike. The .tmpl
// suffix is not decoration: without it the Go tool would try to compile a directory of templates
// whose contents are not Go.
func deriveFile(root, rel string) (string, error) {
	body, err := os.ReadFile(filepath.Join(root, filepath.FromSlash(rel)))
	if err != nil {
		return "", err
	}

	target := tokenise(rel) + ".tmpl"
	// Parsing here rather than leaving it to the first person who generates a module: a rule that
	// produces something text/template cannot read is a bug in the derivation, and this is where
	// the derivation can still name the file it came from.
	if err := parses(target, tokenise(string(body))); err != nil {
		return "", err
	}

	path := filepath.Join(root, filepath.FromSlash(TemplateDir), filepath.FromSlash(target))
	if err := os.MkdirAll(filepath.Dir(path), 0o755); err != nil {
		return "", err
	}
	if err := os.WriteFile(path, []byte(tokenise(string(body))), 0o644); err != nil {
		return "", err
	}
	return target, nil
}

// parses checks a derived template is one text/template can read, with the same functions the
// renderer provides.
func parses(name, body string) error {
	funcs := template.FuncMap{
		"upper":        strings.ToUpper,
		"article":      func(string) string { return "a" },
		"articleUpper": func(string) string { return "A" },
	}
	if _, err := template.New(name).Funcs(funcs).Parse(body); err != nil {
		return fmt.Errorf("the derivation produced a template nothing can read: %w", err)
	}
	return nil
}

package gen_test

import (
	"fmt"
	"os"
	"path/filepath"
	"sort"
	"strings"
	"testing"

	"github.com/SekiroKenjii/kakehashi/tools/cli/internal/gen"
	"github.com/SekiroKenjii/kakehashi/tools/cli/internal/gensync"
	"github.com/SekiroKenjii/kakehashi/tools/cli/internal/unitfile"
)

// repository is the template repository this package is a part of, four levels up from here.
const repository = "../../../.."

// example is the module the generator was derived from, named as the template repository spells
// it: the identity fields are the placeholders themselves, so rendering with them reproduces the
// files on disk rather than a scaffolded copy of them.
func example() gen.Data {
	return gen.Data{
		ID:       "notes",
		Module:   "Notes",
		Entity:   "Note",
		Variable: "note",
		Icon:     "note",
		Glyph:    `\uE70B`,
		Title:    "Notes",

		AppName:       "__APP_NAME__",
		AppNameLower:  "__APP_NAME_LOWER__",
		AppNameUpper:  "__APP_NAME_UPPER__",
		AppTitle:      "__APP_TITLE__",
		RootNamespace: "__ROOT_NAMESPACE__",
		ProtoPackage:  "__PROTO_PACKAGE__",
		GoModule:      "__GO_MODULE__",
		Accent:        "__ACCENT__",
		Author:        "__AUTHOR__",
		Year:          "__YEAR__",
	}
}

// The drift test docs/pivot/04-PHASE-3-GENERATORS.md §4 asks for. The example module is the source
// of truth for what a generated module looks like; this is what makes that true rather than
// aspirational. It fails the moment somebody edits the example without re-deriving, and the fix is
// to run gen-sync and commit.
func TestRenderingWithTheExamplesNamesReproducesTheExample(t *testing.T) {
	module, err := gen.Render(example())
	if err != nil {
		t.Fatalf("Render: %v", err)
	}
	if len(module.Files) == 0 {
		t.Fatal("the generator rendered no files")
	}

	for _, file := range module.Files {
		want, err := os.ReadFile(filepath.Join(repository, filepath.FromSlash(file.Path)))
		if err != nil {
			t.Errorf("%s: the generator produces a file the example module does not have: %v", file.Path, err)
			continue
		}
		if file.Body != string(want) {
			t.Errorf("%s has drifted from the example module.\nRun 'go run ./cmd/gen-sync' and commit.\n%s",
				file.Path, firstDifference(file.Body, string(want)))
		}
	}
}

// The other direction: every file the example unit claims is part of the module has to be a file
// the generator can produce, or a generated module is missing something the example has.
func TestTheGeneratorCoversEveryFileOfTheExample(t *testing.T) {
	module, err := gen.Render(example())
	if err != nil {
		t.Fatalf("Render: %v", err)
	}

	produced := map[string]bool{}
	for _, file := range module.Files {
		produced[file.Path] = true
	}

	for _, path := range examplePaths(t) {
		if strings.HasPrefix(path, "server/internal/gen/") {
			continue // buf writes this one, not a template
		}
		for _, file := range filesUnder(t, path) {
			if !produced[file] {
				t.Errorf("%s is part of the example module and the generator does not produce it", file)
			}
		}
	}
}

// Rendering with a name that is not the example's must leave no trace of the example's name, in a
// path or in a body. A rule that only works for the word "notes" would pass the reproduction test
// above and fail here.
func TestRenderingAnotherModuleLeavesNoTraceOfTheExample(t *testing.T) {
	data := example()
	data.ID, data.Module, data.Entity, data.Variable, data.Title = "orders", "Orders", "Order", "order", "Orders"
	data.Icon, data.Glyph = "document", `\uE8A5`
	data.AppName, data.RootNamespace = "SmokeApp", "SmokeApp"
	data.ProtoPackage, data.GoModule = "smokeapp", "example.com/smokeapp"

	module, err := gen.Render(data)
	if err != nil {
		t.Fatalf("Render: %v", err)
	}

	for _, file := range module.Files {
		if left := gensync.Untokenised(file.Path); len(left) > 0 {
			t.Errorf("%s: the path still names the example module: %v", file.Path, left)
		}
		if left := gensync.Untokenised(file.Body); len(left) > 0 {
			t.Errorf("%s: the body still names the example module: %v", file.Path, left)
		}
	}
	for _, site := range module.Wiring {
		if left := gensync.Untokenised(strings.Join(append(site.Lines, site.File), "\n")); len(left) > 0 {
			t.Errorf("%s: the wiring still names the example module: %v", site.File, left)
		}
	}

	// "an order", not "a order".
	for _, file := range module.Files {
		if strings.Contains(file.Body, "a order") {
			t.Errorf("%s: generated prose reads 'a order'", file.Path)
		}
	}
}

func TestRenderProducesTheWiringAndTheGeneratedPaths(t *testing.T) {
	module, err := gen.Render(example())
	if err != nil {
		t.Fatalf("Render: %v", err)
	}

	if len(module.Wiring) == 0 {
		t.Error("the generator contributes no wiring, so a generated module would not be mounted")
	}
	for _, site := range module.Wiring {
		if site.File == "" || site.Section == "" || len(site.Lines) == 0 {
			t.Errorf("incomplete wiring site: %+v", site)
		}
	}
	if len(module.Generated) == 0 {
		t.Error("the generator names no generated tree, so removal would leave the stubs behind")
	}
}

func examplePaths(t *testing.T) []string {
	t.Helper()
	unit, err := unitfile.Load(filepath.Join(repository, filepath.FromSlash(gensync.ExampleUnit)))
	if err != nil {
		t.Fatal(err)
	}

	paths := append([]string{}, unit.Paths...)
	sort.Strings(paths)
	return paths
}

func filesUnder(t *testing.T, rel string) []string {
	t.Helper()
	at := filepath.Join(repository, filepath.FromSlash(rel))
	info, err := os.Stat(at)
	if err != nil {
		t.Fatalf("the example unit names %s: %v", rel, err)
	}
	if !info.IsDir() {
		return []string{rel}
	}

	var found []string
	err = filepath.WalkDir(at, func(path string, entry os.DirEntry, err error) error {
		if err != nil || entry.IsDir() {
			return err
		}
		from, err := filepath.Rel(filepath.Join(repository), path)
		if err != nil {
			return err
		}
		found = append(found, filepath.ToSlash(from))
		return nil
	})
	if err != nil {
		t.Fatal(err)
	}
	return found
}

// firstDifference points at the line that drifted rather than printing two files.
func firstDifference(got, want string) string {
	g, w := strings.Split(got, "\n"), strings.Split(want, "\n")
	for i := 0; i < len(g) && i < len(w); i++ {
		if g[i] != w[i] {
			return fmt.Sprintf("line %d:\n  rendered: %s\n  example:  %s", i+1, g[i], w[i])
		}
	}
	return fmt.Sprintf("the files differ in length: %d lines rendered, %d in the example", len(g), len(w))
}

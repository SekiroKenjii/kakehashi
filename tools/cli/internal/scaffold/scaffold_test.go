package scaffold_test

import (
	"flag"
	"io/fs"
	"os"
	"path/filepath"
	"runtime"
	"sort"
	"strings"
	"testing"
	"time"

	"github.com/SekiroKenjii/kakehashi/tools/cli/internal/manifest"
	"github.com/SekiroKenjii/kakehashi/tools/cli/internal/scaffold"
	"github.com/SekiroKenjii/kakehashi/tools/cli/internal/template"
)

var update = flag.Bool("update", false, "rewrite the golden trees from what the scaffold produces")

const fixture = "testdata/fixture"

func inputs() scaffold.Inputs {
	return scaffold.Inputs{
		AppName:     "SmokeApp",
		AppTitle:    "Smoke App",
		GoModule:    "example.com/smokeapp",
		Author:      "Smoke",
		Year:        "2026",
		WithExample: true,
	}
}

// options builds a scaffold of the fixture into a fresh directory. Every test needs the same five
// lines, and none of them is what the test is about.
func options(t *testing.T, in scaffold.Inputs) scaffold.Options {
	t.Helper()
	return optionsFrom(t, in, fixture)
}

// optionsFrom is options against a copy of the fixture, for the tests that break something in it
// first. The descriptor has to come from the same tree, or the test edits a file nobody reads.
func optionsFrom(t *testing.T, in scaffold.Inputs, source string) scaffold.Options {
	t.Helper()
	in.Derive(time.Date(2026, 1, 1, 0, 0, 0, 0, time.UTC))

	descriptor, err := template.LoadDescriptor(source, "1.1.0")
	if err != nil {
		t.Fatalf("LoadDescriptor: %v", err)
	}
	return scaffold.Options{
		Source:     source,
		Dest:       filepath.Join(t.TempDir(), "project"),
		Descriptor: descriptor,
		Inputs:     in,
		Origin:     "github.com/SekiroKenjii/kakehashi",
		Version:    "9.9.9",
		CLIVersion: "1.1.0",
	}
}

func TestScaffoldMatchesTheGoldenTree(t *testing.T) {
	cases := []struct {
		golden      string
		withExample bool
	}{
		{"testdata/golden/full", true},
		{"testdata/golden/bare", false},
	}
	for _, c := range cases {
		t.Run(filepath.Base(c.golden), func(t *testing.T) {
			in := inputs()
			in.WithExample = c.withExample

			opts := options(t, in)
			if _, err := scaffold.Run(opts); err != nil {
				t.Fatalf("Run: %v", err)
			}
			compareTrees(t, c.golden, opts.Dest)
		})
	}
}

func TestManifestRecordsTheScaffold(t *testing.T) {
	in := inputs()
	in.WithExample = false

	opts := options(t, in)
	if _, err := scaffold.Run(opts); err != nil {
		t.Fatalf("Run: %v", err)
	}

	m, err := manifest.Load(filepath.Join(opts.Dest, manifest.Name))
	if err != nil {
		t.Fatalf("Load: %v", err)
	}
	if m.Template.Version != "9.9.9" || m.Template.Source != opts.Origin {
		t.Errorf("template = %+v", m.Template)
	}
	// The template's own half of the compatibility matrix, copied out of the descriptor: a
	// generator running here later has no template tree to read it from.
	if m.Template.RequiresCLI != opts.Descriptor.RequiresCLI {
		t.Errorf("requiresCli = %q, want the descriptor's %q",
			m.Template.RequiresCLI, opts.Descriptor.RequiresCLI)
	}
	if m.CLI.Version != "1.1.0" {
		t.Errorf("cli = %+v", m.CLI)
	}
	if m.Inputs.AppName != "SmokeApp" || m.Inputs.ProtoPackage != "smokeapp" || m.Inputs.WithExample {
		t.Errorf("inputs = %+v", m.Inputs)
	}
	if len(m.Units.Removed) != 1 || m.Units.Removed[0] != "example" {
		t.Errorf("removed = %v, want [example]", m.Units.Removed)
	}
	if len(m.Units.Applied) != 0 {
		t.Errorf("applied = %v, want none", m.Units.Applied)
	}
	if m.CreatedAt.IsZero() {
		t.Error("createdAt is zero")
	}
}

// The manifest is the one file allowed to name the generator, so the self-check has to let it
// through and nothing else may join it.
func TestTheOnlyMentionOfTheTemplateIsTheManifest(t *testing.T) {
	opts := options(t, inputs())
	if _, err := scaffold.Run(opts); err != nil {
		t.Fatalf("Run: %v", err)
	}

	var named []string
	err := filepath.WalkDir(opts.Dest, func(path string, entry fs.DirEntry, err error) error {
		if err != nil {
			return err
		}
		if entry.IsDir() {
			if entry.Name() == ".git" {
				return fs.SkipDir
			}
			return nil
		}

		body, err := os.ReadFile(path)
		if err != nil {
			return err
		}
		rel, _ := filepath.Rel(opts.Dest, path)
		for _, line := range strings.Split(string(body), "\n") {
			if !strings.Contains(strings.ToLower(line), "kakehashi") {
				continue
			}
			if strings.Contains(line, "kakehashi:unit-") || filepath.Base(rel) == manifest.Name {
				continue
			}
			named = append(named, filepath.ToSlash(rel)+": "+line)
		}
		return nil
	})
	if err != nil {
		t.Fatal(err)
	}
	if len(named) > 0 {
		t.Errorf("the project names the template outside the manifest:\n%s", strings.Join(named, "\n"))
	}
}

func TestDryRunWritesNothing(t *testing.T) {
	opts := options(t, inputs())
	opts.DryRun = true

	result, err := scaffold.Run(opts)
	if err != nil {
		t.Fatalf("Run: %v", err)
	}
	if result.Substituted == 0 {
		t.Error("a dry run reported no substitutions, so it did not do the work it is checking")
	}
	assertNothingBehind(t, opts.Dest)
}

func TestDestinationMustBeEmpty(t *testing.T) {
	opts := options(t, inputs())
	if err := os.MkdirAll(opts.Dest, 0o755); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(filepath.Join(opts.Dest, "occupied"), []byte("x"), 0o644); err != nil {
		t.Fatal(err)
	}

	if _, err := scaffold.Run(opts); err == nil {
		t.Fatal("Run scaffolded into a directory that was not empty")
	}
	if entries, _ := os.ReadDir(opts.Dest); len(entries) != 1 {
		t.Errorf("the destination was touched: %v", entries)
	}
}

// A unit file that names a path the tree does not have is the failure the atomic workflow exists
// for: it happens after the tree has been staged and part of it removed.
func TestAFailureLeavesNothingBehind(t *testing.T) {
	source := copyTree(t, fixture)
	if err := os.RemoveAll(filepath.Join(source, "server", "internal", "modules", "example")); err != nil {
		t.Fatal(err)
	}

	in := inputs()
	in.WithExample = false
	opts := optionsFrom(t, in, source)

	if _, err := scaffold.Run(opts); err == nil {
		t.Fatal("Run finished with a unit file that disagrees with the tree")
	}
	assertNothingBehind(t, opts.Dest)
}

func TestSelfCheckCatchesASurvivingPlaceholder(t *testing.T) {
	source := copyTree(t, fixture)
	leftover := filepath.Join(source, "docs", "leftover.md")
	if err := os.WriteFile(leftover, []byte("__UNKNOWN_PLACEHOLDER__\n"), 0o644); err != nil {
		t.Fatal(err)
	}

	opts := optionsFrom(t, inputs(), source)

	_, err := scaffold.Run(opts)
	if err == nil {
		t.Fatal("Run handed over a tree that still carries a placeholder")
	}
	if !strings.Contains(err.Error(), "leftover.md") {
		t.Errorf("the refusal does not name the file: %v", err)
	}
	assertNothingBehind(t, opts.Dest)
}

// The redaction that lets an input carry the template's name must not be able to erase the name
// from lines that have nothing to do with it. A one-letter proto package is the shortest lever.
func TestSelfCheckSurvivesAShortInputValue(t *testing.T) {
	source := copyTree(t, fixture)
	leak := filepath.Join(source, "docs", "leak.md")
	if err := os.WriteFile(leak, []byte("Built with the Kakehashi template.\n"), 0o644); err != nil {
		t.Fatal(err)
	}

	in := inputs()
	in.ProtoPackage = "a"

	opts := optionsFrom(t, in, source)

	_, err := scaffold.Run(opts)
	if err == nil {
		t.Fatal("a one-letter input turned the identity check off")
	}
	if !strings.Contains(err.Error(), "leak.md") {
		t.Errorf("the refusal does not name the file: %v", err)
	}
}

// A scaffolded project runs the CLI, records where it came from, and carries the generator's
// markers. All three spell the tool's name, and none of them is the template's identity leaking.
func TestSelfCheckAllowsTheCliNamedAsATool(t *testing.T) {
	source := copyTree(t, fixture)
	tool := filepath.Join(source, "docs", "tool.md")
	body := "Add one with `kakehashi add module orders`, take it back with\n" +
		"`kakehashi remove module orders`. The record is .kakehashi.json.\n" +
		"// kakehashi:module-registrations:begin\n"
	if err := os.WriteFile(tool, []byte(body), 0o644); err != nil {
		t.Fatal(err)
	}

	opts := optionsFrom(t, inputs(), source)
	if _, err := scaffold.Run(opts); err != nil {
		t.Fatalf("Run refused a project that names the tool it is run with: %v", err)
	}
}

// The exemption is the tool, not the line it is on. A command does not license the sentence beside
// it to keep calling the project by the template's name.
func TestSelfCheckStillCatchesTheTemplateOnALineThatRunsTheTool(t *testing.T) {
	source := copyTree(t, fixture)
	tool := filepath.Join(source, "docs", "tool.md")
	if err := os.WriteFile(tool,
		[]byte("Run `kakehashi add module orders` in your Kakehashi project.\n"), 0o644); err != nil {
		t.Fatal(err)
	}

	opts := optionsFrom(t, inputs(), source)

	_, err := scaffold.Run(opts)
	if err == nil {
		t.Fatal("a command on the line turned the identity check off for the rest of it")
	}
	if !strings.Contains(err.Error(), "tool.md") {
		t.Errorf("the refusal does not name the file: %v", err)
	}
}

// A project whose own module path contains the template's owner is allowed to say so.
func TestSelfCheckAllowsTheTemplatesNameInsideAnInput(t *testing.T) {
	in := inputs()
	in.GoModule = "github.com/SekiroKenjii/smokeapp"

	opts := options(t, in)
	if _, err := scaffold.Run(opts); err != nil {
		t.Fatalf("Run: %v", err)
	}

	body, err := os.ReadFile(filepath.Join(opts.Dest, "server", "go.mod"))
	if err != nil {
		t.Fatal(err)
	}
	if !strings.Contains(string(body), "github.com/SekiroKenjii/smokeapp/server") {
		t.Errorf("go.mod = %q", body)
	}
}

func TestAuthBrowserRewritesTheSetting(t *testing.T) {
	in := inputs()
	in.Auth = scaffold.AuthBrowser

	opts := options(t, in)
	if _, err := scaffold.Run(opts); err != nil {
		t.Fatalf("Run: %v", err)
	}

	body, err := os.ReadFile(filepath.Join(opts.Dest, "client", "src", "SmokeApp.App", "appsettings.json"))
	if err != nil {
		t.Fatal(err)
	}
	if !strings.Contains(string(body), `"Mode": "Browser"`) {
		t.Errorf("appsettings.json = %s", body)
	}
	// The rest of the file has to survive the edit, numbers included.
	if !strings.Contains(string(body), `"TimeoutSeconds": 30`) {
		t.Errorf("the edit changed a setting it was not asked about:\n%s", body)
	}
}

// A descriptor is data the scaffold acts on with RemoveAll and Rename, and a template comes over
// the network. A path that climbs out of the tree is refused whether it is hostile or a typo.
func TestDescriptorPathsCannotReachOutsideTheTemplate(t *testing.T) {
	source := copyTree(t, fixture)
	descriptor := filepath.Join(source, filepath.FromSlash(template.DescriptorName))
	body, err := os.ReadFile(descriptor)
	if err != nil {
		t.Fatal(err)
	}
	escaped := strings.Replace(string(body), `"TEMPLATE-ONLY.md"`, `"../precious"`, 1)
	if escaped == string(body) {
		t.Fatal("the fixture descriptor changed shape; this test edits its exclude list")
	}
	if err := os.WriteFile(descriptor, []byte(escaped), 0o644); err != nil {
		t.Fatal(err)
	}

	opts := optionsFrom(t, inputs(), source)

	precious := filepath.Join(filepath.Dir(opts.Dest), "precious")
	if err := os.MkdirAll(precious, 0o755); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(filepath.Join(precious, "data.txt"), []byte("keep me"), 0o644); err != nil {
		t.Fatal(err)
	}

	if _, err := scaffold.Run(opts); err == nil {
		t.Fatal("Run accepted a descriptor path that leaves the template")
	}
	if _, err := os.Stat(filepath.Join(precious, "data.txt")); err != nil {
		t.Errorf("a file outside the scaffold was removed: %v", err)
	}
}

// An end marker before its begin balances the count, so nothing catches it downstream.
func TestATransposedMarkerPairIsRefused(t *testing.T) {
	source := copyTree(t, fixture)
	marker := filepath.Join(source, "server", "cmd", "server", "main.go")
	body, err := os.ReadFile(marker)
	if err != nil {
		t.Fatal(err)
	}
	transposed := strings.Replace(string(body),
		"// kakehashi:unit-example:begin\n\tkernel.Mount(example.New())\n\t// kakehashi:unit-example:end",
		"// kakehashi:unit-example:end\n\tkernel.Mount(example.New())\n\t// kakehashi:unit-example:begin", 1)
	if transposed == string(body) {
		t.Fatal("the fixture changed shape; this test transposes a marker pair")
	}
	if err := os.WriteFile(marker, []byte(transposed), 0o644); err != nil {
		t.Fatal(err)
	}

	in := inputs()
	in.WithExample = false
	opts := optionsFrom(t, in, source)

	_, err = scaffold.Run(opts)
	if err == nil {
		t.Fatal("Run removed a region between a transposed marker pair without saying so")
	}
	if !strings.Contains(err.Error(), "precedes") {
		t.Errorf("the refusal does not say what is wrong: %v", err)
	}
}

// The destination is ./<name> by default, but --dir can name a path several levels deep.
func TestARunThatDoesNotFinishTakesItsDirectoriesWithIt(t *testing.T) {
	root := t.TempDir()
	opts := options(t, inputs())
	opts.Dest = filepath.Join(root, "deep", "nested", "project")
	opts.DryRun = true

	if _, err := scaffold.Run(opts); err != nil {
		t.Fatalf("Run: %v", err)
	}
	if _, err := os.Stat(filepath.Join(root, "deep")); !os.IsNotExist(err) {
		t.Errorf("a dry run left the destination's parents behind: %v", err)
	}
}

func assertNothingBehind(t *testing.T, dest string) {
	t.Helper()
	if _, err := os.Stat(dest); !os.IsNotExist(err) {
		t.Errorf("%s exists after a run that should not have created it", dest)
	}

	entries, err := os.ReadDir(filepath.Dir(dest))
	if err != nil {
		t.Fatal(err)
	}
	for _, entry := range entries {
		if strings.HasPrefix(entry.Name(), ".kakehashi-") {
			t.Errorf("a working directory was left behind: %s", entry.Name())
		}
	}
}

func copyTree(t *testing.T, src string) string {
	t.Helper()
	dst := filepath.Join(t.TempDir(), "source")

	err := filepath.WalkDir(src, func(path string, entry fs.DirEntry, err error) error {
		if err != nil {
			return err
		}
		rel, err := filepath.Rel(src, path)
		if err != nil {
			return err
		}
		target := filepath.Join(dst, rel)
		if entry.IsDir() {
			return os.MkdirAll(target, 0o755)
		}

		body, err := os.ReadFile(path)
		if err != nil {
			return err
		}
		info, err := entry.Info()
		if err != nil {
			return err
		}
		return os.WriteFile(target, body, info.Mode().Perm())
	})
	if err != nil {
		t.Fatal(err)
	}
	return dst
}

// compareTrees checks the scaffolded tree against the golden one, file by file. The repository and
// the manifest are skipped: one is not part of the tree and the other carries a timestamp.
func compareTrees(t *testing.T, golden, got string) {
	t.Helper()
	if *update {
		if err := os.RemoveAll(golden); err != nil {
			t.Fatal(err)
		}
		if err := os.MkdirAll(filepath.Dir(golden), 0o755); err != nil {
			t.Fatal(err)
		}
	}

	actual := readTree(t, got)
	if *update {
		for rel, file := range actual {
			target := filepath.Join(golden, filepath.FromSlash(rel))
			if err := os.MkdirAll(filepath.Dir(target), 0o755); err != nil {
				t.Fatal(err)
			}
			if err := os.WriteFile(target, file.body, file.mode); err != nil {
				t.Fatal(err)
			}
		}
		t.Logf("rewrote %s from %d files", golden, len(actual))
		return
	}

	want := readTree(t, golden)
	for _, rel := range union(want, actual) {
		expected, ok := want[rel]
		if !ok {
			t.Errorf("%s: the scaffold produced a file the golden tree does not have", rel)
			continue
		}
		produced, ok := actual[rel]
		if !ok {
			t.Errorf("%s: missing from the scaffolded tree", rel)
			continue
		}
		if string(produced.body) != string(expected.body) {
			t.Errorf("%s:\n got: %q\nwant: %q", rel, produced.body, expected.body)
		}
		if runtime.GOOS != "windows" && produced.mode&0o111 != expected.mode&0o111 {
			t.Errorf("%s: mode %v, want %v", rel, produced.mode, expected.mode)
		}
	}
}

type file struct {
	body []byte
	mode fs.FileMode
}

func readTree(t *testing.T, root string) map[string]file {
	t.Helper()
	tree := map[string]file{}

	err := filepath.WalkDir(root, func(path string, entry fs.DirEntry, err error) error {
		if err != nil {
			return err
		}
		if entry.IsDir() {
			if entry.Name() == ".git" {
				return fs.SkipDir
			}
			return nil
		}

		rel, err := filepath.Rel(root, path)
		if err != nil {
			return err
		}
		if filepath.Base(rel) == manifest.Name {
			return nil
		}

		body, err := os.ReadFile(path)
		if err != nil {
			return err
		}
		info, err := entry.Info()
		if err != nil {
			return err
		}
		tree[filepath.ToSlash(rel)] = file{body: body, mode: info.Mode().Perm()}
		return nil
	})
	if err != nil {
		t.Fatal(err)
	}
	return tree
}

func union(a, b map[string]file) []string {
	seen := map[string]bool{}
	for rel := range a {
		seen[rel] = true
	}
	for rel := range b {
		seen[rel] = true
	}

	all := make([]string, 0, len(seen))
	for rel := range seen {
		all = append(all, rel)
	}
	sort.Strings(all)
	return all
}

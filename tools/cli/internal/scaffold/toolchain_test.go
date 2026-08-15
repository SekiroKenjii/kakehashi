package scaffold

import (
	"os"
	"path/filepath"
	"testing"
)

// A restore writes the working directory's absolute path into obj/project.assets.json, and that
// path carries the generator's name. Leaving it there fails the self-check on every machine with
// a .NET SDK; shipping it points the first build at a directory that is gone.
func TestCleanBuildOutput(t *testing.T) {
	work := t.TempDir()
	removed := []string{
		filepath.Join("client", "src", "App", "obj", "project.assets.json"),
		filepath.Join("client", "src", "App", "bin", "Release", "App.dll"),
		filepath.Join(".buf-cache", "modules", "cache.bin"),
	}
	kept := []string{
		filepath.Join("client", "src", "App", "App.csproj"),
		filepath.Join("server", "cmd", "server", "main.go"),
	}

	for _, rel := range append(append([]string{}, removed...), kept...) {
		path := filepath.Join(work, rel)
		if err := os.MkdirAll(filepath.Dir(path), 0o755); err != nil {
			t.Fatal(err)
		}
		if err := os.WriteFile(path, []byte("x"), 0o644); err != nil {
			t.Fatal(err)
		}
	}

	if err := cleanBuildOutput(work); err != nil {
		t.Fatalf("cleanBuildOutput: %v", err)
	}
	for _, rel := range removed {
		if _, err := os.Stat(filepath.Join(work, rel)); !os.IsNotExist(err) {
			t.Errorf("%s survived: %v", rel, err)
		}
	}
	for _, rel := range kept {
		if _, err := os.Stat(filepath.Join(work, rel)); err != nil {
			t.Errorf("%s was removed: %v", rel, err)
		}
	}
}

// The working directory is built inside a path the caller chose, and a bracket in a directory name
// is a character class to a glob.
func TestSolutionInReadsRatherThanGlobs(t *testing.T) {
	client := filepath.Join(t.TempDir(), "Jane [Work]", "client")
	if err := os.MkdirAll(client, 0o755); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(filepath.Join(client, "OrderDesk.slnx"), []byte("<Solution/>"), 0o644); err != nil {
		t.Fatal(err)
	}

	if got := solutionIn(client); got != "OrderDesk.slnx" {
		t.Errorf("solutionIn = %q, want OrderDesk.slnx", got)
	}
	if got := solutionIn(filepath.Join(client, "nothing-here")); got != "" {
		t.Errorf("solutionIn of a missing directory = %q", got)
	}
}

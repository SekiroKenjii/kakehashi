package generate

import (
	"os"
	"path/filepath"
	"testing"
)

// The transaction is what makes every pipeline in this package atomic, so what it can put back is
// what "rolled back completely" means. A failure can land after a file was created, after one was
// edited, after a tree was deleted, or after all three.
func TestRollbackPutsEverythingBack(t *testing.T) {
	root := t.TempDir()
	writeFile(t, root, "server/main.go", "package main\n")
	writeFile(t, root, "docs/keep/README.md", "kept\n")

	tx := newTx(root)
	if err := tx.Create("server/modules/orders/api.go", "package api\n"); err != nil {
		t.Fatalf("Create: %v", err)
	}
	if err := tx.Edit("server/main.go", func(string) (string, error) { return "package main // orders\n", nil }); err != nil {
		t.Fatalf("Edit: %v", err)
	}
	if err := tx.Delete("docs/keep"); err != nil {
		t.Fatalf("Delete: %v", err)
	}

	// Half-way through, everything is different.
	if body := read(t, root, "server/main.go"); body != "package main // orders\n" {
		t.Fatalf("the edit did not happen: %q", body)
	}

	if err := tx.Rollback(); err != nil {
		t.Fatalf("Rollback: %v", err)
	}

	if body := read(t, root, "server/main.go"); body != "package main\n" {
		t.Errorf("the edited file was not restored: %q", body)
	}
	if body := read(t, root, "docs/keep/README.md"); body != "kept\n" {
		t.Errorf("the deleted tree was not restored: %q", body)
	}
	// The created file goes, and so do the directories that existed only to hold it.
	for _, gone := range []string{"server/modules/orders/api.go", "server/modules/orders", "server/modules"} {
		if _, err := os.Stat(filepath.Join(root, filepath.FromSlash(gone))); !os.IsNotExist(err) {
			t.Errorf("%s survived the rollback", gone)
		}
	}
	if _, err := os.Stat(filepath.Join(root, "server")); err != nil {
		t.Errorf("a directory that was already there was taken with it: %v", err)
	}
}

// Creating over an existing file would make a rollback a guess about what was there before.
func TestCreateRefusesToOverwrite(t *testing.T) {
	root := t.TempDir()
	writeFile(t, root, "server/main.go", "package main\n")

	if err := newTx(root).Create("server/main.go", "other"); err == nil {
		t.Error("Create overwrote a file that was already there")
	}
	if body := read(t, root, "server/main.go"); body != "package main\n" {
		t.Errorf("the file was changed anyway: %q", body)
	}
}

// An edit is remembered once, so a second edit of the same file rolls back to what it held before
// the first — which is what happens when a module is wired into two sections of one file.
func TestEditRemembersTheOriginalNotTheIntermediate(t *testing.T) {
	root := t.TempDir()
	writeFile(t, root, "catalog.cs", "original\n")

	tx := newTx(root)
	for _, body := range []string{"first\n", "second\n"} {
		if err := tx.Edit("catalog.cs", func(string) (string, error) { return body, nil }); err != nil {
			t.Fatalf("Edit: %v", err)
		}
	}
	if err := tx.Rollback(); err != nil {
		t.Fatalf("Rollback: %v", err)
	}

	if body := read(t, root, "catalog.cs"); body != "original\n" {
		t.Errorf("rolled back to an intermediate state: %q", body)
	}
}

func writeFile(t *testing.T, root, rel, body string) {
	t.Helper()
	path := filepath.Join(root, filepath.FromSlash(rel))
	if err := os.MkdirAll(filepath.Dir(path), 0o755); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(path, []byte(body), 0o644); err != nil {
		t.Fatal(err)
	}
}

func read(t *testing.T, root, rel string) string {
	t.Helper()
	body, err := os.ReadFile(filepath.Join(root, filepath.FromSlash(rel)))
	if err != nil {
		t.Fatalf("%s: %v", rel, err)
	}
	return string(body)
}

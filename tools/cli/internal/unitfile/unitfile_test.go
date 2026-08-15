package unitfile_test

import (
	"os"
	"path/filepath"
	"testing"

	"github.com/SekiroKenjii/kakehashi/tools/cli/internal/unitfile"
)

const notes = `{
  "schemaVersion": 1,
  "id": "notes",
  "title": "Notes — the reference feature module",
  "description": "One vertical slice across both halves.",
  "paths": ["server/internal/modules/notes/", "client/src/Modules/Notes/"],
  "markers": [
    { "file": "server/cmd/server/main.go", "sections": ["module-imports", "module-registrations"] }
  ]
}
`

func write(t *testing.T, dir, name, body string) string {
	t.Helper()
	path := filepath.Join(dir, name)
	if err := os.WriteFile(path, []byte(body), 0o644); err != nil {
		t.Fatal(err)
	}
	return path
}

func TestLoad(t *testing.T) {
	u, err := unitfile.Load(write(t, t.TempDir(), "notes.json", notes))
	if err != nil {
		t.Fatalf("Load: %v", err)
	}

	if u.ID != "notes" {
		t.Errorf("id = %q, want notes", u.ID)
	}
	if len(u.Paths) != 2 {
		t.Errorf("paths = %v, want 2", u.Paths)
	}
	if len(u.Markers) != 1 || u.Markers[0].File != "server/cmd/server/main.go" {
		t.Errorf("markers = %+v", u.Markers)
	}
	if len(u.Markers[0].Sections) != 2 {
		t.Errorf("sections = %v, want 2", u.Markers[0].Sections)
	}
}

// The unit file in the template is the one the CLI has to read for real.
func TestLoadDirReadsTheTemplatesOwnUnits(t *testing.T) {
	units, err := unitfile.LoadDir(filepath.Join("..", "..", "..", "..", "templates", "units"))
	if err != nil {
		t.Fatalf("LoadDir: %v", err)
	}
	if len(units) == 0 {
		t.Fatal("the template declares no removable units")
	}

	found := false
	for _, u := range units {
		if u.ID == "notes" {
			found = true
		}
	}
	if !found {
		t.Errorf("the notes unit is missing from %v", units)
	}
}

func TestLoadDirIsEmptyWhenTheDirectoryIsMissing(t *testing.T) {
	units, err := unitfile.LoadDir(filepath.Join(t.TempDir(), "nothing-here"))
	if err != nil {
		t.Fatalf("LoadDir: %v", err)
	}
	if len(units) != 0 {
		t.Errorf("units = %v, want none", units)
	}
}

func TestLoadRefusals(t *testing.T) {
	cases := []struct {
		name string
		body string
	}{
		{"future schema", `{"schemaVersion": 2, "id": "notes", "paths": ["x"]}`},
		{"no id", `{"schemaVersion": 1, "paths": ["x"]}`},
		{"id that is not a marker-safe token", `{"schemaVersion": 1, "id": "Notes!", "paths": ["x"]}`},
		{"removes nothing", `{"schemaVersion": 1, "id": "notes"}`},
		{"not json", `{`},
	}
	for _, c := range cases {
		t.Run(c.name, func(t *testing.T) {
			if _, err := unitfile.Load(write(t, t.TempDir(), "u.json", c.body)); err == nil {
				t.Errorf("Load accepted a unit file that %s", c.name)
			}
		})
	}
}

func TestRegionMatchesTheMarkerInAnyCommentSyntax(t *testing.T) {
	u, err := unitfile.Load(write(t, t.TempDir(), "notes.json", notes))
	if err != nil {
		t.Fatal(err)
	}

	begin, end := u.Region()
	for _, line := range []string{
		"// kakehashi:unit-notes:begin",
		"<!-- kakehashi:unit-notes:begin -->",
		"  /* kakehashi:unit-notes:begin */",
	} {
		if !begin.MatchString(line) {
			t.Errorf("begin marker did not match %q", line)
		}
	}
	if !end.MatchString("// kakehashi:unit-notes:end") {
		t.Error("end marker did not match")
	}
	if begin.MatchString("// kakehashi:unit-orders:begin") {
		t.Error("the notes marker matched another unit's marker")
	}
}

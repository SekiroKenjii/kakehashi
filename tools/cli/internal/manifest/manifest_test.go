package manifest_test

import (
	"os"
	"path/filepath"
	"reflect"
	"testing"
	"time"

	"github.com/SekiroKenjii/kakehashi/tools/cli/internal/manifest"
)

func sample() *manifest.Manifest {
	return &manifest.Manifest{
		Template:  manifest.Template{Source: "github.com/SekiroKenjii/kakehashi", Version: "0.3.0"},
		CLI:       manifest.CLI{Version: "0.2.1"},
		CreatedAt: time.Date(2026, 9, 1, 10, 0, 0, 0, time.UTC),
		Inputs: manifest.Inputs{
			AppName:       "OrderDesk",
			AppTitle:      "Order Desk",
			RootNamespace: "OrderDesk",
			GoModule:      "github.com/me/orderdesk",
			ProtoPackage:  "orderdesk",
			Accent:        "#E34234",
			Author:        "Me",
			Year:          "2026",
			Auth:          "inapp",
			WithExample:   true,
		},
		Units: manifest.Units{Applied: []string{"notes"}, Removed: []string{}},
	}
}

func TestRoundTrip(t *testing.T) {
	path := filepath.Join(t.TempDir(), manifest.Name)
	want := sample()
	if err := want.Write(path); err != nil {
		t.Fatalf("Write: %v", err)
	}

	got, err := manifest.Load(path)
	if err != nil {
		t.Fatalf("Load: %v", err)
	}
	if !reflect.DeepEqual(got, want) {
		t.Errorf("round trip changed the manifest:\n got %+v\nwant %+v", got, want)
	}
}

// The schema in docs/pivot/03-PHASE-2-CLI.md §2, byte for byte, including the trailing newline and
// the empty list that must not serialise as null.
func TestWriteMatchesTheDocumentedShape(t *testing.T) {
	path := filepath.Join(t.TempDir(), manifest.Name)
	if err := sample().Write(path); err != nil {
		t.Fatalf("Write: %v", err)
	}

	body, err := os.ReadFile(path)
	if err != nil {
		t.Fatal(err)
	}

	want := `{
  "schemaVersion": 1,
  "template": {
    "source": "github.com/SekiroKenjii/kakehashi",
    "version": "0.3.0"
  },
  "cli": {
    "version": "0.2.1"
  },
  "createdAt": "2026-09-01T10:00:00Z",
  "inputs": {
    "appName": "OrderDesk",
    "appTitle": "Order Desk",
    "rootNamespace": "OrderDesk",
    "goModule": "github.com/me/orderdesk",
    "protoPackage": "orderdesk",
    "accent": "#E34234",
    "author": "Me",
    "year": "2026",
    "auth": "inapp",
    "withExample": true
  },
  "units": {
    "applied": [
      "notes"
    ],
    "removed": []
  }
}
`
	if string(body) != want {
		t.Errorf("manifest on disk:\n%s\nwant:\n%s", body, want)
	}
}

func TestWriteNormalisesEmptyUnitLists(t *testing.T) {
	path := filepath.Join(t.TempDir(), manifest.Name)
	m := sample()
	m.Units = manifest.Units{}
	if err := m.Write(path); err != nil {
		t.Fatalf("Write: %v", err)
	}

	got, err := manifest.Load(path)
	if err != nil {
		t.Fatalf("Load: %v", err)
	}
	if got.Units.Applied == nil || got.Units.Removed == nil {
		t.Errorf("empty unit lists round-tripped as null: %+v", got.Units)
	}
}

func TestLoadRefusesAFutureSchema(t *testing.T) {
	path := filepath.Join(t.TempDir(), manifest.Name)
	if err := os.WriteFile(path, []byte(`{"schemaVersion": 99}`), 0o644); err != nil {
		t.Fatal(err)
	}
	if _, err := manifest.Load(path); err == nil {
		t.Error("Load accepted a schema version it cannot know the shape of")
	}
}

func TestLoadRefusesAMissingSchemaVersion(t *testing.T) {
	path := filepath.Join(t.TempDir(), manifest.Name)
	if err := os.WriteFile(path, []byte(`{"template": {"version": "0.1.0"}}`), 0o644); err != nil {
		t.Fatal(err)
	}
	if _, err := manifest.Load(path); err == nil {
		t.Error("Load accepted a manifest with no schema version")
	}
}

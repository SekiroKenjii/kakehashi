package template

import (
	"errors"
	"os"
	"path/filepath"
	"strings"
	"testing"
)

func templateDir(t *testing.T, body string) string {
	t.Helper()
	dir := t.TempDir()
	path := filepath.Join(dir, filepath.FromSlash(DescriptorName))
	if err := os.MkdirAll(filepath.Dir(path), 0o755); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(path, []byte(body), 0o644); err != nil {
		t.Fatal(err)
	}
	return dir
}

func TestLoadDescriptor(t *testing.T) {
	dir := templateDir(t, `{
	  "schemaVersion": 1,
	  "templateVersion": "0.3.0",
	  "requiresCli": ">=0.2 <0.4",
	  "markersSchema": 1,
	  "unitsSchema": 1,
	  "exclude": ["docs/pivot"],
	  "move": [{"from": "templates/README.scaffold.md", "to": "README.md"}]
	}`)

	d, err := LoadDescriptor(dir, "0.2.1")
	if err != nil {
		t.Fatalf("LoadDescriptor: %v", err)
	}
	if d.TemplateVersion != "0.3.0" {
		t.Errorf("templateVersion = %s", d.TemplateVersion)
	}
	if d.Units != "templates/units" {
		t.Errorf("units = %q, want the default", d.Units)
	}
	if len(d.Exclude) != 1 || len(d.Move) != 1 {
		t.Errorf("descriptor = %+v", d)
	}
}

// The compatibility matrix is checked in both directions, and a refusal has to say which of the
// two to move.
func TestLoadDescriptorChecksTheCompatibilityRange(t *testing.T) {
	dir := templateDir(t, `{
	  "schemaVersion": 1,
	  "templateVersion": "0.3.0",
	  "requiresCli": ">=0.2 <0.4",
	  "markersSchema": 1,
	  "unitsSchema": 1
	}`)

	for _, ok := range []string{"0.2.0", "0.3.9"} {
		if _, err := LoadDescriptor(dir, ok); err != nil {
			t.Errorf("LoadDescriptor with cli %s: %v", ok, err)
		}
	}
	for _, bad := range []string{"0.1.9", "0.4.0"} {
		_, err := LoadDescriptor(dir, bad)
		if err == nil {
			t.Fatalf("LoadDescriptor accepted cli %s", bad)
		}
		if !strings.Contains(err.Error(), "0.3.0") || !strings.Contains(err.Error(), bad) {
			t.Errorf("refusal for cli %s does not name both versions: %v", bad, err)
		}
	}
}

// The other direction: a template outside the range this CLI declares is refused whatever it says
// about the CLI, and the refusal names the CLI as the thing to change.
func TestLoadDescriptorChecksTheTemplateAgainstTheRangeTheCliDeclares(t *testing.T) {
	dir := templateDir(t, `{
	  "schemaVersion": 1,
	  "templateVersion": "1.0.0",
	  "requiresCli": ">=0.1",
	  "markersSchema": 1,
	  "unitsSchema": 1
	}`)

	_, err := LoadDescriptor(dir, "0.1.0")
	if err == nil {
		t.Fatal("LoadDescriptor accepted a template past the range this CLI declares")
	}
	if !errors.Is(err, ErrIncompatible) {
		t.Errorf("the refusal is not an incompatibility, so Resolve will not look further back: %v", err)
	}
	for _, want := range []string{SupportedTemplates, "1.0.0", "upgrade the CLI"} {
		if !strings.Contains(err.Error(), want) {
			t.Errorf("refusal does not mention %q: %v", want, err)
		}
	}
}

func TestLoadDescriptorRefusals(t *testing.T) {
	cases := []struct {
		name string
		body string
	}{
		{"a schema this CLI does not know", `{"schemaVersion": 99, "markersSchema": 1, "unitsSchema": 1}`},
		{"a marker vocabulary it does not speak", `{"schemaVersion": 1, "markersSchema": 2, "unitsSchema": 1}`},
		{"a unit format it does not parse", `{"schemaVersion": 1, "markersSchema": 1, "unitsSchema": 2}`},
		{"a range that is not a range", `{"schemaVersion": 1, "templateVersion": "0.1.0",
		  "markersSchema": 1, "unitsSchema": 1, "requiresCli": "newest"}`},
		{"not json", `{`},
	}
	for _, c := range cases {
		t.Run(c.name, func(t *testing.T) {
			if _, err := LoadDescriptor(templateDir(t, c.body), "0.1.0"); err == nil {
				t.Errorf("LoadDescriptor accepted %s", c.name)
			}
		})
	}
}

func TestLoadDescriptorSaysWhenADirectoryIsNotATemplate(t *testing.T) {
	_, err := LoadDescriptor(t.TempDir(), "0.1.0")
	if err == nil || !strings.Contains(err.Error(), "not a kakehashi template") {
		t.Errorf("error = %v", err)
	}
}

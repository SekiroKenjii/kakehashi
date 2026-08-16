package project_test

import (
	"os"
	"path/filepath"
	"strings"
	"testing"

	"github.com/SekiroKenjii/kakehashi/tools/cli/internal/manifest"
	"github.com/SekiroKenjii/kakehashi/tools/cli/internal/project"
)

// scaffolded writes the manifest a project of this template version would carry.
func scaffolded(t *testing.T, templateVersion, requiresCLI string) string {
	t.Helper()
	root := t.TempDir()
	m := &manifest.Manifest{
		Template: manifest.Template{
			Source:      "github.com/owner/repo",
			Version:     templateVersion,
			RequiresCLI: requiresCLI,
		},
		CLI:    manifest.CLI{Version: "0.1.0"},
		Inputs: manifest.Inputs{AppName: "SmokeApp", GoModule: "example.com/smokeapp"},
	}
	if err := m.Write(filepath.Join(root, manifest.Name)); err != nil {
		t.Fatal(err)
	}
	return root
}

func TestOpenFindsTheManifestFromAnywhereInTheTree(t *testing.T) {
	root := scaffolded(t, "0.1.0", ">=0.1.0 <0.2.0")
	deep := filepath.Join(root, "server", "internal", "modules")
	if err := os.MkdirAll(deep, 0o755); err != nil {
		t.Fatal(err)
	}

	p, err := project.Open(deep, "0.1.0")
	if err != nil {
		t.Fatalf("Open: %v", err)
	}
	if p.Root != root {
		t.Errorf("root = %s, want %s", p.Root, root)
	}
	if p.Manifest.Inputs.AppName != "SmokeApp" {
		t.Errorf("manifest = %+v", p.Manifest)
	}
}

// The matrix from the project's side, in the direction the CLI declares.
func TestOpenRefusesATemplatePastTheRangeTheCliGeneratesInto(t *testing.T) {
	root := scaffolded(t, "1.4.0", ">=0.1.0")

	_, err := project.Open(root, "0.1.0")
	if err == nil {
		t.Fatal("Open accepted a project the generator cannot write into")
	}
	for _, want := range []string{"1.4.0", "upgrade the CLI"} {
		if !strings.Contains(err.Error(), want) {
			t.Errorf("refusal does not mention %q: %v", want, err)
		}
	}
}

// And in the direction the template declares. The two are separate refusals because the remedy is
// not the same: one asks for a newer CLI, this one for a particular range of them.
func TestOpenRefusesACliOutsideWhatTheTemplateAskedFor(t *testing.T) {
	root := scaffolded(t, "0.3.0", ">=0.2.0 <0.4.0")

	_, err := project.Open(root, "0.5.0")
	if err == nil {
		t.Fatal("Open accepted a CLI the project's template excludes")
	}
	for _, want := range []string{"0.3.0", ">=0.2.0 <0.4.0", "0.5.0", "change the CLI"} {
		if !strings.Contains(err.Error(), want) {
			t.Errorf("refusal does not mention %q: %v", want, err)
		}
	}
}

// A project scaffolded before the manifest recorded the range is still workable: the range the CLI
// declares already bounds how far apart the two can be.
func TestOpenAcceptsAManifestWithNoRecordedRange(t *testing.T) {
	root := scaffolded(t, "0.1.0", "")

	if _, err := project.Open(root, "0.9.0"); err != nil {
		t.Fatalf("Open refused a manifest written before requiresCli was recorded: %v", err)
	}
}

func TestOpenSaysWhenThereIsNoProject(t *testing.T) {
	_, err := project.Open(t.TempDir(), "0.1.0")
	if err == nil || !strings.Contains(err.Error(), manifest.Name) {
		t.Errorf("error = %v, want one naming the manifest it looked for", err)
	}
}

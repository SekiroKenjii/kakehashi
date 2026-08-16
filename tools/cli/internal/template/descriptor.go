package template

import (
	"encoding/json"
	"errors"
	"fmt"
	"os"
	"path/filepath"

	"github.com/SekiroKenjii/kakehashi/tools/cli/internal/semver"
	"github.com/SekiroKenjii/kakehashi/tools/cli/internal/unitfile"
)

// DescriptorName is the descriptor's path inside a template tree.
const DescriptorName = "templates/template.json"

// DescriptorSchema is the descriptor format this CLI understands.
const DescriptorSchema = 1

// MarkersSchema is the marker vocabulary this CLI speaks: `kakehashi:unit-<id>:begin` and its end.
// A template that changes the vocabulary raises its own markersSchema, and this refuses it rather
// than silently leaving wiring behind.
const MarkersSchema = 1

// SupportedTemplates is the range of template versions this CLI understands, and is the CLI's half
// of the compatibility matrix in docs/pivot/06-PHASE-5-RELEASE.md §1.2. The template's half is its
// own requiresCli, and both are checked: either side can be the one that moved, and a refusal is
// only useful if it says which.
//
// The schema numbers are the other, coarser half of the same question. They catch a template whose
// format this binary cannot read at all; this catches one it could read and should not, because the
// two versioned apart.
const SupportedTemplates = ">=1.0.0 <2.0.0"

// Descriptor is templates/template.json: the template's own account of its version, the CLI range
// it works with, and the parts of itself that belong to the template repository rather than to a
// project made from it. It lives in the template rather than in this binary because the two
// version separately — a template that adds a template-only directory has to be able to say so
// without a new CLI release.
type Descriptor struct {
	SchemaVersion   int             `json:"schemaVersion"`
	TemplateVersion string          `json:"templateVersion"`
	RequiresCLI     string          `json:"requiresCli"`
	MarkersSchema   int             `json:"markersSchema"`
	UnitsSchema     int             `json:"unitsSchema"`
	Units           string          `json:"units"`
	ExampleUnits    []string        `json:"exampleUnits"`
	Exclude         []string        `json:"exclude"`
	ExcludeLines    []LineExclusion `json:"excludeLines"`
	Move            []Move          `json:"move"`
	Auth            *AuthSetting    `json:"auth"`
}

// LineExclusion drops every line of a file that contains one of the substrings. It is how an index
// page loses the entries for pages the scaffold removes.
type LineExclusion struct {
	File  string   `json:"file"`
	Match []string `json:"match"`
}

// Move renames a path after the exclusions run, which is how the scaffold README becomes the
// project's README.
type Move struct {
	From string `json:"from"`
	To   string `json:"to"`
}

// AuthSetting names the JSON setting that carries the sign-in mode, and the value each CLI choice
// writes there. A template that has no such setting omits it, and --auth is then refused.
type AuthSetting struct {
	File  string            `json:"file"`
	Path  []string          `json:"path"`
	Modes map[string]string `json:"modes"`
}

// LoadDescriptor reads the descriptor out of a template tree and checks that this CLI can work
// with it in both directions: the template's schemas against what this binary understands, and
// this binary's version against the range the template requires.
func LoadDescriptor(root, cliVersion string) (*Descriptor, error) {
	path := filepath.Join(root, filepath.FromSlash(DescriptorName))
	body, err := os.ReadFile(path)
	if err != nil {
		return nil, fmt.Errorf("%s is not a kakehashi template: %w", root, err)
	}

	var d Descriptor
	if err := json.Unmarshal(body, &d); err != nil {
		return nil, fmt.Errorf("parse %s: %w", path, err)
	}
	if d.SchemaVersion != DescriptorSchema {
		return nil, fmt.Errorf("%s: schemaVersion %d needs a different kakehashi", path, d.SchemaVersion)
	}
	if d.MarkersSchema != MarkersSchema {
		return nil, fmt.Errorf("%s: markersSchema %d needs a different kakehashi", path, d.MarkersSchema)
	}
	if d.UnitsSchema != unitfile.SchemaVersion {
		return nil, fmt.Errorf("%s: unitsSchema %d needs a different kakehashi", path, d.UnitsSchema)
	}
	if d.Units == "" {
		d.Units = "templates/units"
	}
	if err := d.supported(); err != nil {
		return nil, err
	}
	if err := d.allows(cliVersion); err != nil {
		return nil, err
	}
	return &d, nil
}

// ErrIncompatible reports a template this CLI is outside the range of. Resolve tells it apart from
// every other failure because it is the one worth trying an older release for.
var ErrIncompatible = errors.New("incompatible template")

// supported checks the template's version against the range this CLI declares. It is the direction
// the template cannot state for itself: a template released after this binary knows nothing about
// what this binary can read.
func (d *Descriptor) supported() error {
	allowed, err := semver.ParseRange(SupportedTemplates)
	if err != nil {
		return err
	}
	have, err := semver.Parse(d.TemplateVersion)
	if err != nil {
		return fmt.Errorf("template version %q: %w", d.TemplateVersion, err)
	}
	if !allowed.Allows(have) {
		return fmt.Errorf("%w: this kakehashi works with templates %s and this one is %s — "+
			"upgrade the CLI, or name an older template with --template-version",
			ErrIncompatible, SupportedTemplates, d.TemplateVersion)
	}
	return nil
}

// allows checks this CLI against the template's requiresCli range. It is the other direction, and
// the refusal names the CLI because the CLI is the side that has to move.
func (d *Descriptor) allows(cliVersion string) error {
	if d.RequiresCLI == "" {
		return nil
	}

	want, err := semver.ParseRange(d.RequiresCLI)
	if err != nil {
		return fmt.Errorf("template %s: requiresCli %q: %w", d.TemplateVersion, d.RequiresCLI, err)
	}
	have, err := semver.Parse(cliVersion)
	if err != nil {
		return fmt.Errorf("cli version %q: %w", cliVersion, err)
	}
	if !want.Allows(have) {
		return fmt.Errorf("%w: template %s needs kakehashi %s and this is %s — change the CLI, "+
			"not the template", ErrIncompatible, d.TemplateVersion, d.RequiresCLI, cliVersion)
	}
	return nil
}

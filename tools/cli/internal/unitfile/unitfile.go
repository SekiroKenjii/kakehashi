// Package unitfile parses templates/units/*.json, the machine-readable description of a removable
// unit: the paths that are only there for it, and the files whose marker regions wire it in.
// Parsing only — the tree surgery those two lists describe belongs to the scaffold engine.
package unitfile

import (
	"encoding/json"
	"fmt"
	"os"
	"path/filepath"
	"sort"
)

// SchemaVersion is the unit format this package understands.
const SchemaVersion = 1

// Unit is one templates/units/*.json file.
type Unit struct {
	SchemaVersion int      `json:"schemaVersion"`
	ID            string   `json:"id"`
	Title         string   `json:"title"`
	Description   string   `json:"description"`
	Paths         []string `json:"paths"`
	Markers       []Marker `json:"markers"`
}

// Marker names a file that wires the unit in, and the marker sections inside it. The sections are
// documentation for a reader and for a verifier: removal keys off the unit's own marker, which
// carries the id.
type Marker struct {
	File     string   `json:"file"`
	Sections []string `json:"sections"`
}

// Load reads and validates one unit file.
func Load(path string) (*Unit, error) {
	body, err := os.ReadFile(path)
	if err != nil {
		return nil, err
	}

	var u Unit
	if err := json.Unmarshal(body, &u); err != nil {
		return nil, fmt.Errorf("parse %s: %w", path, err)
	}
	if u.SchemaVersion != SchemaVersion {
		return nil, fmt.Errorf("%s: unsupported schemaVersion %d", path, u.SchemaVersion)
	}
	if u.ID == "" {
		return nil, fmt.Errorf("%s: no id", path)
	}
	if !idPattern.MatchString(u.ID) {
		return nil, fmt.Errorf("%s: id %q must match %s", path, u.ID, idPattern)
	}
	if len(u.Paths) == 0 && len(u.Markers) == 0 {
		return nil, fmt.Errorf("%s: unit %s removes nothing", path, u.ID)
	}
	return &u, nil
}

// LoadDir reads every unit file in a directory, ordered by id. A directory that does not exist
// holds no units, which is what a template with nothing removable looks like.
func LoadDir(dir string) ([]*Unit, error) {
	matches, err := filepath.Glob(filepath.Join(dir, "*.json"))
	if err != nil {
		return nil, err
	}

	units := make([]*Unit, 0, len(matches))
	for _, path := range matches {
		u, err := Load(path)
		if err != nil {
			return nil, err
		}
		units = append(units, u)
	}
	sort.Slice(units, func(i, j int) bool { return units[i].ID < units[j].ID })
	return units, nil
}

// Package manifest reads and writes .kakehashi.json, the record a scaffolded project keeps of the
// template it came from and the inputs it was made with. That file is the only place a generated
// project is allowed to name the generator, and it is what a later upgrade reads to reproduce the
// scaffold, so every input that reaches a placeholder is written down.
package manifest

import (
	"encoding/json"
	"fmt"
	"os"
	"time"
)

// Name is the manifest's file name, at the root of a scaffolded project.
const Name = ".kakehashi.json"

// SchemaVersion is the format this package writes. A reader refuses anything newer.
const SchemaVersion = 1

// Manifest is the whole of .kakehashi.json.
type Manifest struct {
	SchemaVersion int       `json:"schemaVersion"`
	Template      Template  `json:"template"`
	CLI           CLI       `json:"cli"`
	CreatedAt     time.Time `json:"createdAt"`
	Inputs        Inputs    `json:"inputs"`
	Units         Units     `json:"units"`
}

// Template identifies the template release the project was scaffolded from.
type Template struct {
	Source  string `json:"source"`
	Version string `json:"version"`

	// RequiresCLI is the template's own half of the compatibility matrix, copied here at scaffold
	// time. A generator running in this project has no template tree to read it out of — the
	// project is not one — so without this the check could only run one way.
	RequiresCLI string `json:"requiresCli,omitempty"`
}

// CLI records the generator version, which is what tells a bug report which binary produced a tree.
type CLI struct {
	Version string `json:"version"`
}

// Inputs is every answer the scaffold consumed. Reproducing a scaffold means feeding these back in,
// so a new input belongs here on the day it is added.
type Inputs struct {
	AppName       string `json:"appName"`
	AppTitle      string `json:"appTitle"`
	RootNamespace string `json:"rootNamespace"`
	GoModule      string `json:"goModule"`
	ProtoPackage  string `json:"protoPackage"`
	Accent        string `json:"accent"`
	Author        string `json:"author"`
	Year          string `json:"year"`
	Auth          string `json:"auth"`
	WithExample   bool   `json:"withExample"`
}

// Units records which removable units the project kept and which the scaffold took out.
type Units struct {
	Applied []string `json:"applied"`
	Removed []string `json:"removed"`
}

// Load reads a manifest from disk.
func Load(path string) (*Manifest, error) {
	body, err := os.ReadFile(path)
	if err != nil {
		return nil, err
	}

	var m Manifest
	if err := json.Unmarshal(body, &m); err != nil {
		return nil, fmt.Errorf("parse %s: %w", path, err)
	}
	if m.SchemaVersion > SchemaVersion {
		return nil, fmt.Errorf("%s: schemaVersion %d needs a newer kakehashi", path, m.SchemaVersion)
	}
	if m.SchemaVersion < 1 {
		return nil, fmt.Errorf("%s: schemaVersion %d is not a version", path, m.SchemaVersion)
	}
	return &m, nil
}

// Write serialises the manifest, stamping the schema version and normalising the unit lists so an
// empty one round-trips as [] rather than as null.
func (m *Manifest) Write(path string) error {
	m.SchemaVersion = SchemaVersion
	if m.Units.Applied == nil {
		m.Units.Applied = []string{}
	}
	if m.Units.Removed == nil {
		m.Units.Removed = []string{}
	}

	body, err := json.MarshalIndent(m, "", "  ")
	if err != nil {
		return err
	}
	return os.WriteFile(path, append(body, '\n'), 0o644)
}

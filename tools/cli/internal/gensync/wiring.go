package gensync

import (
	"encoding/json"
	"fmt"
	"os"
	"path/filepath"
	"regexp"
	"strings"

	"github.com/SekiroKenjii/kakehashi/tools/cli/internal/marker"
)

// Plan is what the derivation writes beside the templates: the lines a module carves into the
// files that know every module, and the tree the code generator produces rather than a template.
type Plan struct {
	Wiring []Site `json:"wiring"`

	// Paths is what a module *is*, at the granularity the example unit file states it: a directory
	// where the whole directory belongs to the module, a file where it does not. A record written
	// from the rendered file list instead would miss anything added to the module afterwards — a
	// page, for one — and removal would leave it behind.
	Paths     []string `json:"paths"`
	Generated []string `json:"generated"`
}

// Site is one insertion: a file, the section inside it, and the lines the module contributes.
type Site struct {
	File    string   `json:"file"`
	Section string   `json:"section"`
	Sorted  bool     `json:"sorted"`
	Lines   []string `json:"lines"`
}

var (
	sectionEdge = regexp.MustCompile(`kakehashi:(module-[a-z-]+):(begin|end)`)
	unitEdge    = regexp.MustCompile(`kakehashi:unit-([a-z0-9-]+):(begin|end)`)
)

// deriveWiring reads a module's own blocks back out of a file that wires it in, so the lines the
// generator writes are the lines the example already has rather than a second guess at them.
func deriveWiring(root, file, id string) ([]Site, error) {
	body, err := os.ReadFile(filepath.Join(root, filepath.FromSlash(file)))
	if err != nil {
		return nil, fmt.Errorf("the example unit names %s as a marker file: %w", file, err)
	}

	var sites []Site
	section := ""
	collecting := false
	var lines []string

	for _, line := range strings.Split(string(body), "\n") {
		if edge := sectionEdge.FindStringSubmatch(line); edge != nil {
			if edge[2] == "begin" {
				section = edge[1]
			} else {
				section = ""
			}
			continue
		}

		edge := unitEdge.FindStringSubmatch(line)
		switch {
		case edge != nil && edge[1] == id && edge[2] == "begin":
			collecting, lines = true, nil
		case edge != nil && edge[1] == id && edge[2] == "end":
			if section == "" {
				return nil, fmt.Errorf("%s: a block of %s sits outside any module section", file, id)
			}
			sites = append(sites, Site{
				File:    tokenise(file),
				Section: section,
				Sorted:  marker.Sorted(section),
				Lines:   dedent(lines),
			})
			collecting = false
		case collecting:
			lines = append(lines, line)
		}
	}

	if len(sites) == 0 {
		return nil, fmt.Errorf("%s: the example unit names it, but it carries no %s block", file, id)
	}
	return sites, nil
}

// dedent strips the indentation the block is written at and tokenises what is left. The marker
// engine puts the section's own indentation back when it inserts.
func dedent(lines []string) []string {
	indent := ""
	for _, line := range lines {
		if strings.TrimSpace(line) == "" {
			continue
		}
		lead := line[:len(line)-len(strings.TrimLeft(line, " \t"))]
		if indent == "" || len(lead) < len(indent) {
			indent = lead
		}
	}

	out := make([]string, 0, len(lines))
	for _, line := range lines {
		out = append(out, tokenise(strings.TrimPrefix(line, indent)))
	}
	return out
}

func writePlan(root string, plan Plan) error {
	if plan.Generated == nil {
		plan.Generated = []string{}
	}
	if plan.Paths == nil {
		plan.Paths = []string{}
	}
	if plan.Wiring == nil {
		plan.Wiring = []Site{}
	}

	body, err := json.MarshalIndent(plan, "", "  ")
	if err != nil {
		return err
	}
	return os.WriteFile(filepath.Join(root, filepath.FromSlash(PlanFile)), append(body, '\n'), 0o644)
}

// Package marker edits the marker regions that wire a module into the files that know every
// module: composition roots, solution files, project references.
//
// Two vocabularies live here. A section — `kakehashi:module-imports` and its kind — fences the
// region a generator may write in. A unit — `kakehashi:unit-orders` — fences one module's lines
// inside a region, so removing the module takes back exactly what adding it wrote. Nothing outside
// a section is ever touched.
package marker

import (
	"fmt"
	"path/filepath"
	"regexp"
	"strings"
)

// The sections the template fences. A generator writes into these and nowhere else.
const (
	SectionImports       = "module-imports"
	SectionRegistrations = "module-registrations"
	SectionIDs           = "module-ids"
	SectionProjects      = "module-projects"
	SectionTestProjects  = "module-test-projects"
)

// Sorted reports whether a section's entries are kept in order.
//
// Imports and project references are: the formatters that gate this repository sort them, so an
// entry in the wrong place fails a build. Registrations and the id list are not — the first is
// ordered by what boots before what, and appending is the only placement that does not claim to
// know better than the order already there.
func Sorted(section string) bool {
	switch section {
	case SectionImports, SectionProjects, SectionTestProjects:
		return true
	default:
		return false
	}
}

// Style is how a marker is spelled as a comment in one kind of file.
type Style struct {
	Open  string
	Close string
}

// styles covers every file the template fences. A file type absent from this table cannot carry a
// marker, and asking for one is a mistake worth stopping for rather than guessing a syntax.
var styles = map[string]Style{
	".go":     {Open: "//"},
	".cs":     {Open: "//"},
	".proto":  {Open: "//"},
	".xaml":   {Open: "<!--", Close: "-->"},
	".slnx":   {Open: "<!--", Close: "-->"},
	".csproj": {Open: "<!--", Close: "-->"},
	".props":  {Open: "<!--", Close: "-->"},
	".xml":    {Open: "<!--", Close: "-->"},
	".yml":    {Open: "#"},
	".yaml":   {Open: "#"},
	".sql":    {Open: "/*", Close: "*/"},
}

// StyleFor returns the comment syntax a file's markers are written in.
func StyleFor(path string) (Style, error) {
	style, ok := styles[strings.ToLower(filepath.Ext(path))]
	if !ok {
		return Style{}, fmt.Errorf("%s: no marker comment syntax is defined for this file type", path)
	}
	return style, nil
}

// comment renders one marker line.
func (s Style) comment(indent, text string) string {
	line := indent + s.Open + " " + text
	if s.Close != "" {
		line += " " + s.Close
	}
	return line
}

// Unit is one module's fence inside a region.
func Unit(id string) string { return "kakehashi:unit-" + id }

func fence(name, edge string) *regexp.Regexp {
	return regexp.MustCompile(regexp.QuoteMeta(name + ":" + edge))
}

// Has reports whether a body already carries a unit's fence, which is what makes adding a module
// twice an error rather than a second copy of its wiring.
func Has(body, id string) bool {
	return fence(Unit(id), "begin").MatchString(body)
}

// region locates the content between a section's markers, exclusive of the marker lines, and the
// indentation the section is written at.
func region(lines []string, section string) (start, end int, indent string, err error) {
	begin, finish := fence(section, "begin"), fence(section, "end")
	start, end = -1, -1

	for i, line := range lines {
		switch {
		case begin.MatchString(line):
			if start >= 0 {
				return 0, 0, "", fmt.Errorf("section %s begins twice", section)
			}
			start, indent = i, leading(line)
		case finish.MatchString(line):
			if start < 0 {
				return 0, 0, "", fmt.Errorf("section %s ends before it begins", section)
			}
			end = i
		}
		if end >= 0 {
			break
		}
	}
	if start < 0 || end < 0 {
		return 0, 0, "", fmt.Errorf("no %s section in this file", section)
	}
	return start + 1, end, indent, nil
}

func leading(line string) string {
	return line[:len(line)-len(strings.TrimLeft(line, " \t"))]
}

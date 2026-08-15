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

// prefix is the generator's namespace, and the first thing every marker says.
const prefix = "kakehashi:"

// Unit is one module's fence inside a region.
func Unit(id string) string { return prefix + "unit-" + id }

// section is a region's fence.
func section(name string) string { return prefix + name }

// opens and closes are every comment syntax a marker is written in, longest first so that "<!--"
// is not read as the start of something shorter.
var (
	opens  = []string{"<!--", "//", "/*", "#"}
	closes = []string{"-->", "*/"}
)

// name reads the marker a line *is*, and returns nothing for a line that merely mentions one.
//
// The distinction is load-bearing. The composition roots explain their own markers in prose —
// "the markers below — kakehashi:module-imports:begin and its kind — delimit the wiring a
// generator writes" — and a match anywhere in the line would read that sentence as a second
// opening of the section.
func name(line string) string {
	text := strings.TrimSpace(line)
	for _, open := range opens {
		if after, found := strings.CutPrefix(text, open); found {
			text = after
			break
		}
	}
	for _, close := range closes {
		text = strings.TrimSuffix(text, close)
	}

	text = strings.TrimSpace(text)
	if !strings.HasPrefix(text, prefix) {
		return ""
	}
	return text
}

// is reports whether a line is exactly the given marker. The marker arrives prefixed, from
// Unit or section, so there is one spelling of it and not two.
func is(line, marker, edge string) bool { return name(line) == marker+":"+edge }

// Has reports whether a body already carries a unit's fence, which is what makes adding a module
// twice an error rather than a second copy of its wiring, and what tells a removal it finished.
func Has(body, id string) bool {
	for _, line := range strings.Split(body, "\n") {
		if is(line, Unit(id), "begin") || is(line, Unit(id), "end") {
			return true
		}
	}
	return false
}

// region locates the content between a section's markers, exclusive of the marker lines, and the
// indentation the section is written at.
func region(lines []string, want string) (start, end int, indent string, err error) {
	start, end = -1, -1

	for i, line := range lines {
		switch {
		case is(line, section(want), "begin"):
			if start >= 0 {
				return 0, 0, "", fmt.Errorf("section %s begins twice", want)
			}
			start, indent = i, leading(line)
		case is(line, section(want), "end"):
			if start < 0 {
				return 0, 0, "", fmt.Errorf("section %s ends before it begins", want)
			}
			end = i
		}
		if end >= 0 {
			break
		}
	}
	if start < 0 || end < 0 {
		return 0, 0, "", fmt.Errorf("no %s section in this file", want)
	}
	return start + 1, end, indent, nil
}

func leading(line string) string {
	return line[:len(line)-len(strings.TrimLeft(line, " \t"))]
}

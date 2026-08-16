package marker

import (
	"fmt"
	"strings"
)

// Insert writes a unit's lines into a section, fenced by the unit's own markers.
//
// Sorted decides where. An import block and a using block are ordered by the formatters that gate
// this repository, so a module inserted in the wrong place fails a build; a registration list is
// ordered by what boots first, and appending is the only placement that does not claim to know
// better. The lines arrive unindented and are written at the section's own indentation.
func Insert(body, section, id string, lines []string, sorted bool, style Style) (string, error) {
	if len(lines) == 0 {
		return "", fmt.Errorf("nothing to insert for %s", id)
	}

	split := strings.Split(body, "\n")
	start, end, indent, err := region(split, section)
	if err != nil {
		return "", err
	}

	// Inside the section rather than across the file: one file often has a module in two sections —
	// the composition root imports it and registers it — and each is its own insertion.
	if Has(strings.Join(split[start:end], "\n"), id) {
		return "", fmt.Errorf("the %s section already wires %s in", section, id)
	}

	block := make([]string, 0, len(lines)+2)
	block = append(block, style.comment(indent, Unit(id)+":begin"))
	for _, line := range lines {
		block = append(block, indent+line)
	}
	block = append(block, style.comment(indent, Unit(id)+":end"))

	at := end
	if sorted {
		at = position(split[start:end], strings.TrimSpace(lines[0])) + start
	}

	out := make([]string, 0, len(split)+len(block))
	out = append(out, split[:at]...)
	out = append(out, block...)
	out = append(out, split[at:]...)
	return strings.Join(out, "\n"), nil
}

// position is where a new entry sorts among what a region already holds. The region is read as a
// list of items — a bare line, or a whole unit block — so an insertion never lands inside another
// module's fence.
func position(region []string, key string) int {
	for i := 0; i < len(region); {
		item, next := itemAt(region, i)
		if item != "" && key < item {
			return i
		}
		i = next
	}
	return len(region)
}

// isUnit reports whether a line is one of a unit's own fences, whichever unit it belongs to.
func isUnit(line string) bool { return strings.HasPrefix(name(line), prefix+"unit-") }

// itemAt reads one item starting at i and returns its sort key and the index after it.
func itemAt(region []string, i int) (key string, next int) {
	if !isUnit(region[i]) {
		return strings.TrimSpace(region[i]), i + 1
	}

	// A unit block sorts by its first content line, and ends at its own end marker.
	key = ""
	for j := i + 1; j < len(region); j++ {
		if isUnit(region[j]) {
			return key, j + 1
		}
		if key == "" {
			key = strings.TrimSpace(region[j])
		}
	}
	return key, len(region)
}

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
	if Has(body, id) {
		return "", fmt.Errorf("this file already wires %s in", id)
	}
	if len(lines) == 0 {
		return "", fmt.Errorf("nothing to insert for %s", id)
	}

	split := strings.Split(body, "\n")
	start, end, indent, err := region(split, section)
	if err != nil {
		return "", err
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

// itemAt reads one item starting at i and returns its sort key and the index after it.
func itemAt(region []string, i int) (key string, next int) {
	line := strings.TrimSpace(region[i])
	if !strings.Contains(line, "kakehashi:unit-") {
		return line, i + 1
	}

	// A unit block sorts by its first content line, and ends at its own end marker.
	key = ""
	for j := i + 1; j < len(region); j++ {
		inner := strings.TrimSpace(region[j])
		if strings.Contains(inner, "kakehashi:unit-") {
			return key, j + 1
		}
		if key == "" {
			key = inner
		}
	}
	return key, len(region)
}

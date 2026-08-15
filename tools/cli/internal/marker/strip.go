package marker

import (
	"fmt"
	"strings"
)

// Strip removes every line a unit fenced, and its fences, wherever they appear in the body. It is
// what removing a module does to the files that wire it in, and what scaffolding a project without
// the example module does to the same files.
func Strip(body, id string) (string, error) {
	unit := Unit(id)

	var kept []string
	depth := 0
	for _, line := range strings.Split(body, "\n") {
		switch {
		case is(line, unit, "begin"):
			depth++
		case is(line, unit, "end"):
			depth--
			// An end before its begin would otherwise take everything between the two out and
			// leave a balanced count behind to say nothing happened.
			if depth < 0 {
				return "", fmt.Errorf("an end marker for %s precedes its begin", id)
			}
		case depth == 0:
			kept = append(kept, line)
		}
	}
	if depth != 0 {
		return "", fmt.Errorf("unbalanced markers for %s", id)
	}
	return strings.Join(kept, "\n"), nil
}

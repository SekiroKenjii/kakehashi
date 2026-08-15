package scaffold

import (
	"fmt"
	"path/filepath"
	"regexp"
	"sort"
	"strings"

	"github.com/SekiroKenjii/kakehashi/tools/cli/internal/manifest"
)

// identityPattern is every spelling of the template's own name, plus any surviving placeholder.
// A tree that still carries one of these is a half-finished scaffold, not a project.
var identityPattern = regexp.MustCompile(`__[A-Z][A-Z0-9_]*__|Kakehashi|kakehashi|KAKEHASHI|SekiroKenjii|架け橋`)

// markerPattern is the generator's namespace rather than the application's: the CLI reads these
// markers in a scaffolded project to add and remove modules, and renaming them would break the
// tool rather than finish the scaffold. It and the manifest are the only two exemptions.
var markerPattern = regexp.MustCompile(`kakehashi:[a-z0-9-]+:(begin|end)`)

// reported caps the list in the error. A scaffold that leaks the template's name leaks it in
// hundreds of places, and the first few are enough to find the cause.
const reported = 20

// selfCheck refuses to hand over a tree that still names the template. Values the caller chose are
// redacted first: a project whose own module path is github.com/SekiroKenjii/orders is allowed to
// say so, and the check would otherwise refuse the one name it cannot object to.
func selfCheck(root string, in Inputs) error {
	redactions := make([]string, 0, len(in.replacements()))
	for _, r := range in.replacements() {
		if r.value != "" {
			redactions = append(redactions, r.value)
		}
	}
	// Longest first: the app name is a substring of the module path that contains it, and removing
	// the short one first leaves the rest of the long one behind for the pattern to find.
	sort.Slice(redactions, func(i, j int) bool { return len(redactions[i]) > len(redactions[j]) })

	var hits []string
	err := walkTextFiles(root, func(rel string, body []byte) error {
		if filepath.Base(rel) == manifest.Name {
			return nil
		}
		for i, line := range strings.Split(string(body), "\n") {
			if markerPattern.MatchString(line) {
				continue
			}
			for _, value := range redactions {
				line = strings.ReplaceAll(line, value, "")
			}
			if match := identityPattern.FindString(line); match != "" {
				hits = append(hits, fmt.Sprintf("%s:%d: %s", filepath.ToSlash(rel), i+1, match))
			}
		}
		return nil
	})
	if err != nil {
		return err
	}
	if len(hits) == 0 {
		return nil
	}

	shown := hits
	if len(shown) > reported {
		shown = shown[:reported]
	}
	more := ""
	if len(hits) > len(shown) {
		more = fmt.Sprintf("\n  ... and %d more", len(hits)-len(shown))
	}
	return fmt.Errorf("the tree still names the template:\n  %s%s", strings.Join(shown, "\n  "), more)
}

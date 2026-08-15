package scaffold

import (
	"fmt"
	"path/filepath"
	"regexp"
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

// selfCheck refuses to hand over a tree that still names the template.
//
// A match inside one of the caller's own values is allowed: a project whose module path is
// github.com/SekiroKenjii/orders is entitled to say so, and the check would otherwise refuse the
// one name it cannot object to.
func selfCheck(root string, in Inputs) error {
	claimed := claims(in)

	var hits []string
	err := walkTextFiles(root, func(rel string, body []byte) error {
		if filepath.Base(rel) == manifest.Name {
			return nil
		}
		for i, line := range strings.Split(string(body), "\n") {
			if markerPattern.MatchString(line) {
				continue
			}
			for _, match := range identityPattern.FindAllStringIndex(line, -1) {
				if covered(line, match, claimed) {
					continue
				}
				hits = append(hits, fmt.Sprintf("%s:%d: %s", filepath.ToSlash(rel), i+1, line[match[0]:match[1]]))
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

// claims is the input values that carry the template's name themselves, and only those: a value
// the pattern would not object to cannot excuse a match, and one that is a substring of the name —
// a single-letter proto package, say — would erase it from every line if this deleted text instead
// of comparing positions.
func claims(in Inputs) []string {
	var claimed []string
	for _, r := range in.replacements() {
		if r.value != "" && identityPattern.MatchString(r.value) {
			claimed = append(claimed, r.value)
		}
	}
	return claimed
}

// covered reports whether the match sits inside an occurrence of one of the caller's values.
func covered(line string, match []int, claimed []string) bool {
	for _, value := range claimed {
		for at := 0; ; {
			index := strings.Index(line[at:], value)
			if index < 0 {
				break
			}
			start := at + index
			if start <= match[0] && match[1] <= start+len(value) {
				return true
			}
			at = start + 1
		}
	}
	return false
}

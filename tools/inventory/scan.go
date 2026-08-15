package main

import (
	"bytes"
	"encoding/csv"
	"fmt"
	"io"
	"os"
	"path/filepath"
	"regexp"
	"strconv"
	"strings"
)

// The two groups the scan can suggest. CORE is never suggested: it is what a file belongs to when
// nothing here matches, and that judgement is the manual half of the inventory.
const (
	groupIdentity = "IDENTITY"
	groupExample  = "EXAMPLE"
)

// rule is one thing worth finding. Name is the CSV's own vocabulary, not the matched text.
type rule struct {
	Name  string
	Group string
	Re    *regexp.Regexp
}

// rules is the pattern table. Each entry runs against the path once and against every line of the
// file, so a directory named for the app and a namespace declaring it both surface.
//
// Adding a placeholder to Phase 1's map means adding its current literal here first: a string this
// table does not know about is a string the rename script will leave behind.
var rules = []rule{
	{"app-name", groupIdentity, regexp.MustCompile(`Kakehashi`)},
	{"app-name-lower", groupIdentity, regexp.MustCompile(`kakehashi`)},
	{"app-name-upper", groupIdentity, regexp.MustCompile(`KAKEHASHI`)},
	{"owner", groupIdentity, regexp.MustCompile(`SekiroKenjii`)},
	{"brand-name-ja", groupIdentity, regexp.MustCompile(`架け橋`)},

	// The torii vermilion, the two reds it shades itself with, the shu it replaced, and the value
	// Phase 1 documents as the default __ACCENT__.
	{"brand-accent", groupIdentity, regexp.MustCompile(`(?i)#(C4513C|A34131|8F3A2B|E0503A|E34234)\b`)},

	// Substring, not word-bounded: the wiring sites are inside identifiers — notesapi, NotesModule,
	// activityv1connect — and a boundary on the right would hide every one of them. The cost is
	// prose ("release notes", "inactivity"), which the manual pass drops.
	{"unit-notes", groupExample, regexp.MustCompile(`(?i)notes`)},
	{"unit-activity", groupExample, regexp.MustCompile(`(?i)activity`)},
}

// scan writes one CSV row per rule per matching line, plus one per rule matching the path itself,
// where the line number is 0.
func scan(root string, files []string, w io.Writer) error {
	out := csv.NewWriter(w)
	defer out.Flush()

	if err := out.Write([]string{"path", "match", "line", "suggested_group"}); err != nil {
		return err
	}

	for _, path := range files {
		for _, r := range rules {
			if m := r.Re.FindString(path); m != "" {
				if err := write(out, path, m, 0, r.Group); err != nil {
					return err
				}
			}
		}

		body, err := os.ReadFile(filepath.Join(root, filepath.FromSlash(path)))
		if err != nil {
			return fmt.Errorf("read %s: %w", path, err)
		}
		// A NUL byte means an image or an icon. Its identity is its content, which no regexp
		// reaches; docs/BOILERPLATE.md classifies those by hand.
		if bytes.IndexByte(body, 0) >= 0 {
			continue
		}

		for i, line := range strings.Split(string(body), "\n") {
			for _, r := range rules {
				if m := r.Re.FindString(line); m != "" {
					if err := write(out, path, m, i+1, r.Group); err != nil {
						return err
					}
				}
			}
		}
	}

	out.Flush()
	return out.Error()
}

func write(out *csv.Writer, path, match string, line int, group string) error {
	return out.Write([]string{path, match, strconv.Itoa(line), group})
}

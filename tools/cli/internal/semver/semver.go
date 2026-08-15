// Package semver parses and compares the version numbers the CLI reads out of other programs and
// out of release tags. It is deliberately small: only the major.minor.patch triple, a prefix of
// "v" or "go", and the comparison operators a range needs.
package semver

import (
	"fmt"
	"regexp"
	"strconv"
	"strings"
)

// Version is a major.minor.patch triple. Pre-release and build metadata are dropped on parse: no
// decision in this tool turns on them, and comparing them correctly is a specification of its own.
type Version struct {
	Major int
	Minor int
	Patch int
}

// number matches the first version-shaped run in a string, so it reads "go1.26.0" out of
// "go version go1.26.0 linux/amd64" and "10.0.100" out of an SDK listing.
var number = regexp.MustCompile(`(\d+)(?:\.(\d+))?(?:\.(\d+))?`)

// Parse reads the first major.minor.patch in s, ignoring any prefix and anything after the triple.
// A missing minor or patch is zero, so "10" parses as 10.0.0.
func Parse(s string) (Version, error) {
	m := number.FindStringSubmatch(s)
	if m == nil {
		return Version{}, fmt.Errorf("no version number in %q", s)
	}

	v := Version{}
	for i, field := range []*int{&v.Major, &v.Minor, &v.Patch} {
		if m[i+1] == "" {
			continue
		}
		n, err := strconv.Atoi(m[i+1])
		if err != nil {
			return Version{}, fmt.Errorf("parse %q: %w", s, err)
		}
		*field = n
	}
	return v, nil
}

// Compare returns -1 when v sorts before w, 0 when they are equal, and 1 when v sorts after w.
func (v Version) Compare(w Version) int {
	for _, pair := range [][2]int{{v.Major, w.Major}, {v.Minor, w.Minor}, {v.Patch, w.Patch}} {
		switch {
		case pair[0] < pair[1]:
			return -1
		case pair[0] > pair[1]:
			return 1
		}
	}
	return 0
}

// AtLeast reports whether v is w or newer.
func (v Version) AtLeast(w Version) bool { return v.Compare(w) >= 0 }

// String renders the triple as major.minor.patch, with no prefix.
func (v Version) String() string { return fmt.Sprintf("%d.%d.%d", v.Major, v.Minor, v.Patch) }

// MustParse is Parse for a constant, and panics on anything it cannot read.
func MustParse(s string) Version {
	v, err := Parse(s)
	if err != nil {
		panic(err)
	}
	return v
}

// Range is a space-separated list of constraints, all of which must hold: ">=0.2 <0.4". An empty
// range accepts everything, which is what a template that states no requirement means.
type Range struct {
	source      string
	constraints []constraint
}

type constraint struct {
	op      string
	version Version
}

var operators = []string{">=", "<=", "!=", ">", "<", "="}

// ParseRange reads a constraint list. Every constraint has to carry an operator: a bare version
// number is ambiguous between "exactly" and "at least", and guessing is how a compatibility matrix
// starts lying.
func ParseRange(s string) (Range, error) {
	r := Range{source: strings.TrimSpace(s)}
	for _, field := range strings.Fields(s) {
		c := constraint{}
		for _, op := range operators {
			if strings.HasPrefix(field, op) {
				c.op = op
				break
			}
		}
		if c.op == "" {
			return Range{}, fmt.Errorf("constraint %q needs one of >=, <=, >, <, =, !=", field)
		}

		v, err := Parse(strings.TrimPrefix(field, c.op))
		if err != nil {
			return Range{}, fmt.Errorf("constraint %q: %w", field, err)
		}
		c.version = v
		r.constraints = append(r.constraints, c)
	}
	return r, nil
}

// Allows reports whether v satisfies every constraint in the range.
func (r Range) Allows(v Version) bool {
	for _, c := range r.constraints {
		cmp := v.Compare(c.version)
		ok := false
		switch c.op {
		case ">=":
			ok = cmp >= 0
		case "<=":
			ok = cmp <= 0
		case ">":
			ok = cmp > 0
		case "<":
			ok = cmp < 0
		case "=":
			ok = cmp == 0
		case "!=":
			ok = cmp != 0
		}
		if !ok {
			return false
		}
	}
	return true
}

// String returns the range as it was written.
func (r Range) String() string { return r.source }

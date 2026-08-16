package semver_test

import (
	"strings"
	"testing"

	"github.com/SekiroKenjii/kakehashi/tools/cli/internal/semver"
)

func TestParse(t *testing.T) {
	cases := []struct {
		in   string
		want string
	}{
		{"1.2.3", "1.2.3"},
		{"v1.2.3", "1.2.3"},
		{"go1.26.0", "1.26.0"},
		{"go version go1.26.0 linux/amd64", "1.26.0"},
		{"10.0.100", "10.0.100"},
		{"10", "10.0.0"},
		{"0.2", "0.2.0"},
		{"template/v0.3.0", "0.3.0"},
		{"1.72.0-rc1", "1.72.0"},
	}
	for _, c := range cases {
		got, err := semver.Parse(c.in)
		if err != nil {
			t.Fatalf("Parse(%q): %v", c.in, err)
		}
		if got.String() != c.want {
			t.Errorf("Parse(%q) = %s, want %s", c.in, got, c.want)
		}
	}

	if _, err := semver.Parse("no digits here"); err == nil {
		t.Error("Parse of a string with no version number returned no error")
	}
}

func TestCompare(t *testing.T) {
	cases := []struct {
		a, b string
		want int
	}{
		{"1.2.3", "1.2.3", 0},
		{"1.2.3", "1.2.4", -1},
		{"1.3.0", "1.2.9", 1},
		{"2.0.0", "10.0.0", -1},
		{"1.26.0", "1.9.0", 1},
	}
	for _, c := range cases {
		got := semver.MustParse(c.a).Compare(semver.MustParse(c.b))
		if got != c.want {
			t.Errorf("Compare(%s, %s) = %d, want %d", c.a, c.b, got, c.want)
		}
	}
}

func TestRangeAllows(t *testing.T) {
	cases := []struct {
		rang  string
		in    string
		allow bool
	}{
		{">=0.2 <0.4", "0.2.0", true},
		{">=0.2 <0.4", "0.3.9", true},
		{">=0.2 <0.4", "0.4.0", false},
		{">=0.2 <0.4", "0.1.9", false},
		{"=1.0.0", "1.0.0", true},
		{"=1.0.0", "1.0.1", false},
		{">1.0.0", "1.0.0", false},
		{"<=1.0.0", "1.0.0", true},
		{"", "9.9.9", true},
	}
	for _, c := range cases {
		r, err := semver.ParseRange(c.rang)
		if err != nil {
			t.Fatalf("ParseRange(%q): %v", c.rang, err)
		}
		if got := r.Allows(semver.MustParse(c.in)); got != c.allow {
			t.Errorf("ParseRange(%q).Allows(%s) = %t, want %t", c.rang, c.in, got, c.allow)
		}
	}
}

func TestParseRangeRefusals(t *testing.T) {
	cases := []struct {
		name  string
		rang  string
		says  string
		valid bool
	}{
		// A bare version number is ambiguous between "exactly" and "at least".
		{"no operator", "0.2", "needs one of", false},
		{"a caret", "^1.2", "needs one of", false},
		// The npm spelling. Parse would read the first number and drop the upper bound silently.
		{"comma separated", ">=0.2.0,<0.4.0", "separated by spaces", false},
		{"trailing text", ">=0.2.0-rc", "not a version number", false},
		{"the spelling that works", ">=0.2.0 <0.4.0", "", true},
	}
	for _, c := range cases {
		t.Run(c.name, func(t *testing.T) {
			_, err := semver.ParseRange(c.rang)
			if c.valid {
				if err != nil {
					t.Errorf("ParseRange(%q): %v", c.rang, err)
				}
				return
			}
			if err == nil {
				t.Fatalf("ParseRange accepted %q", c.rang)
			}
			if !strings.Contains(err.Error(), c.says) {
				t.Errorf("error %q does not say %q", err, c.says)
			}
		})
	}
}

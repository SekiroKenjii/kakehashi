package main

import "testing"

// Every rule in both directions: what it must find, and what it must leave alone. A rule that
// matches everything is as useless as one that matches nothing, and only the second is obvious.
func TestRulesMatchWhatTheyName(t *testing.T) {
	cases := []struct {
		rule  string
		text  string
		match bool
	}{
		{"app-name", "namespace Kakehashi.App.Composition;", true},
		{"app-name", "namespace kakehashi.app;", false},
		{"app-name-lower", "github.com/SekiroKenjii/kakehashi/server", true},
		{"app-name-lower", "Kakehashi.App", false},
		{"app-name-upper", "KAKEHASHI_SQLSERVER_DSN", true},
		{"app-name-upper", "Kakehashi", false},
		{"owner", "module github.com/SekiroKenjii/kakehashi/server", true},
		{"owner", "module example.com/smokeapp", false},
		{"brand-name-ja", "架け橋 — the bridge you build across", true},
		{"brand-name-ja", "the bridge you build across", false},
		{"brand-accent", `<path fill="#C4513C" />`, true},
		{"brand-accent", `<SolidColorBrush Color="#C42B1C" />`, false},
		{"unit-notes", `app.Register(notes.New())`, true},
		{"unit-notes", `new NotesModule(),`, true},
		{"unit-notes", "Note that the kernel refuses this at boot", false},
		{"unit-activity", "activityapi.CanReport", true},
		{"unit-activity", "AppActivityLog", true},
		{"unit-activity", "the kernel's staged boot", false},
	}

	byName := make(map[string]rule, len(rules))
	for _, r := range rules {
		byName[r.Name] = r
	}

	for _, c := range cases {
		r, ok := byName[c.rule]
		if !ok {
			t.Fatalf("no rule named %q", c.rule)
		}
		if got := r.Re.MatchString(c.text); got != c.match {
			t.Errorf("%s on %q = %v, want %v", c.rule, c.text, got, c.match)
		}
	}
}

func TestCoveringPrefersNothingAndReturnsEverything(t *testing.T) {
	entries := []string{"server/internal/app/", "server/cmd/server/main.go", "docs/brand/"}

	cases := []struct {
		path string
		want int
	}{
		{"server/internal/app/kernel.go", 1},
		{"server/cmd/server/main.go", 1},
		{"server/cmd/server/main_test.go", 0},
		{"docs/brand/kakehashi-mark.svg", 1},
	}

	for _, c := range cases {
		if got := len(covering(entries, c.path)); got != c.want {
			t.Errorf("covering(%q) matched %d entries, want %d", c.path, got, c.want)
		}
	}
}

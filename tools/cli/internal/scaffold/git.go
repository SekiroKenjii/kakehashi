package scaffold

import (
	"os/exec"
	"strings"
)

// initRepository turns the scaffolded tree into a repository with one commit, and reports whether
// the commit happened. Git is not a prerequisite — the template is fetched over HTTPS precisely so
// that it need not be — so everything here degrades to a warning.
func initRepository(work, version string, log func(string, ...any)) bool {
	if _, err := exec.LookPath("git"); err != nil {
		log("git is not installed — the project is not a repository yet")
		return false
	}

	// main, because the project's own CI and its breaking check both name that branch.
	steps := [][]string{
		{"init", "-b", "main"},
		{"add", "-A"},
		{"commit", "-m", "chore: scaffold from kakehashi template " + display(version)},
	}
	for _, args := range steps {
		if out, err := run(work, "git", args...); err != nil {
			log("git %s failed, the project is not committed yet:\n%s", args[0], out)
			return false
		}
	}
	return true
}

// GitUserName is the default for --author, and is empty when git is absent or has no name set.
func GitUserName() string {
	out, err := exec.Command("git", "config", "user.name").Output()
	if err != nil {
		return ""
	}
	return strings.TrimSpace(string(out))
}

// display prefixes a version number with v, and leaves a word like "local" alone.
func display(version string) string {
	if version == "" {
		return "(unknown version)"
	}
	if version[0] >= '0' && version[0] <= '9' {
		return "v" + version
	}
	return version
}

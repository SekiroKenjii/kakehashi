package tui

import (
	"os"
	"os/exec"
	"path/filepath"
	"regexp"
	"strings"
	"unicode"

	"github.com/SekiroKenjii/kakehashi/tools/cli/internal/scaffold"
)

// fallbackOwner is the module host used when nothing on the machine says who the author is. It is
// a real module path and builds, and it is obviously not anybody's — which is the point: a default
// nobody would ship reads as one to change, where github.com/<somebody-else> reads as correct.
const fallbackOwner = "example.com"

// suggestedAppName is the current directory read as an app name, and is empty when the directory is
// not one. It is offered rather than filled in: `kakehashi new` is run from the parent of the
// project as often as from a directory prepared for it.
func suggestedAppName() string {
	wd, err := os.Getwd()
	if err != nil {
		return ""
	}

	name := pascal(filepath.Base(wd))
	if scaffold.ValidateAppName(name) != nil {
		return ""
	}
	return name
}

// defaultTitle spaces an app name into words: OrderDesk becomes "Order Desk", and APIGateway
// becomes "API Gateway" rather than "A P I Gateway".
func defaultTitle(appName string) string {
	runes := []rune(appName)
	var out strings.Builder

	for i, r := range runes {
		startsWord := i > 0 && unicode.IsUpper(r) &&
			(!unicode.IsUpper(runes[i-1]) ||
				(i+1 < len(runes) && unicode.IsLower(runes[i+1])))
		if startsWord {
			out.WriteRune(' ')
		}
		out.WriteRune(r)
	}
	return out.String()
}

// defaultGoModule is where a project of this name would live under whoever this machine belongs to.
func defaultGoModule(appName string) string {
	return gitOwner() + "/" + strings.ToLower(appName)
}

// gitOwner is the module path prefix the machine implies, in the order the answers are trustworthy:
// an explicitly configured GitHub user, then the owner of the repository the working directory is
// in, then the author's name reduced to something a module path can hold.
func gitOwner() string {
	if user := gitConfig("github.user"); user != "" {
		return "github.com/" + user
	}
	if owner := remoteOwner(gitConfig("remote.origin.url")); owner != "" {
		return owner
	}
	if slug := slugify(gitConfig("user.name")); slug != "" {
		return "github.com/" + slug
	}
	return fallbackOwner
}

// remoteOwner reduces a remote URL to the host and owner a sibling project would share, for both
// spellings git writes: https://host/owner/repo(.git) and git@host:owner/repo(.git).
func remoteOwner(remote string) string {
	remote = strings.TrimSuffix(strings.TrimSpace(remote), ".git")
	if remote == "" {
		return ""
	}

	if at := strings.Index(remote, "@"); at >= 0 && !strings.Contains(remote, "://") {
		remote = strings.Replace(remote[at+1:], ":", "/", 1)
	}
	if scheme := strings.Index(remote, "://"); scheme >= 0 {
		remote = remote[scheme+len("://"):]
		if at := strings.Index(remote, "@"); at >= 0 {
			remote = remote[at+1:]
		}
	}

	parts := strings.Split(remote, "/")
	if len(parts) < 3 {
		return ""
	}

	prefix := parts[0] + "/" + parts[1]
	if scaffold.ValidateGoModule(prefix) != nil {
		return ""
	}
	return prefix
}

// gitConfig reads one setting, and is empty when git is absent or the setting is not set. Git is
// not a prerequisite of scaffolding, so nothing here may fail the run.
func gitConfig(key string) string {
	out, err := exec.Command("git", "config", "--get", key).Output()
	if err != nil {
		return ""
	}
	return strings.TrimSpace(string(out))
}

var notSlug = regexp.MustCompile(`[^a-z0-9-]+`)

// slugify reduces a person's name to the characters a module path segment may hold.
func slugify(name string) string {
	return strings.Trim(notSlug.ReplaceAllString(strings.ToLower(name), "-"), "-")
}

// pascal turns a directory name into the shape an app name has: the separators a directory is
// allowed drop out, and each word they divided is capitalised.
func pascal(name string) string {
	var out strings.Builder
	upper := true

	for _, r := range name {
		switch {
		case r == '-' || r == '_' || r == ' ' || r == '.':
			upper = true
		case upper:
			out.WriteRune(unicode.ToUpper(r))
			upper = false
		default:
			out.WriteRune(r)
		}
	}
	return out.String()
}

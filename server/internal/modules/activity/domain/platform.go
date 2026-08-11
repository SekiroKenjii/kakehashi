package domain

import "strings"

// PlatformOf reads the operating system family out of a user agent string, or returns empty when it
// says nothing recognisable.
//
// Deliberately shallow. The reader of a feed is answering "was that me?", and for that question
// "Windows" and "iOS" are the whole answer — a browser name and a version number would add
// characters without adding certainty. Full user-agent parsing is a library, a lookup table and a
// maintenance commitment, and it would buy nothing this screen asks for.
//
// It is a function over a stored string rather than a stored column on purpose: it is an opinion
// about text, and an opinion that gets better should get better for the rows already written.
//
// The order of the tests is load-bearing. An iPhone claims "like Mac OS X" and an Android claims
// "Linux", so the specific cases have to be asked before the general ones.
func PlatformOf(userAgent string) string {
	agent := strings.ToLower(userAgent)
	switch {
	case agent == "":
		return ""
	case strings.Contains(agent, "iphone"),
		strings.Contains(agent, "ipad"),
		strings.Contains(agent, "ipod"):
		return "iOS"
	case strings.Contains(agent, "android"):
		return "Android"
	case strings.Contains(agent, "windows"):
		return "Windows"
	case strings.Contains(agent, "mac os"), strings.Contains(agent, "macintosh"):
		// An iPad on a recent iOS claims to be a Macintosh, and nothing in the string contradicts
		// it. Reported as macOS rather than guessed at: a wrong specific answer reads as a fact,
		// and this one would be shown to somebody deciding whether to change their password.
		return "macOS"
	case strings.Contains(agent, "linux"):
		return "Linux"
	default:
		return ""
	}
}

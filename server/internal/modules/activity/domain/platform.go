package domain

import "strings"

// Empty when the agent says nothing recognisable.
//
// Deliberately shallow: the reader is answering "was that me?", and for that "Windows" and "iOS"
// are the whole answer. Full user-agent parsing is a library and a maintenance commitment that
// would buy nothing this screen asks for.
//
// A function over a stored string rather than a stored column, so an opinion that gets better gets
// better for rows already written.
//
// The order of the cases is load-bearing. An iPhone claims "like Mac OS X" and an Android claims
// "Linux", so the specific ones have to be asked before the general ones.
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
		// An iPad on a recent iOS claims to be a Macintosh and nothing contradicts it. Reported as
		// macOS rather than guessed at: a wrong specific answer reads as a fact, and this one is
		// shown to somebody deciding whether to change their password.
		return "macOS"
	case strings.Contains(agent, "linux"):
		return "Linux"
	default:
		return ""
	}
}

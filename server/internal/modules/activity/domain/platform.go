package domain

import "strings"

// PlatformOf reads the operating system family out of a user agent string, or returns empty when
// it says nothing recognisable.
//
// Deliberately shallow: the reader of a feed is answering "was that me?", and "Windows" or "iOS"
// is the whole answer. It is a function over the stored string rather than a stored column, so an
// improved parse applies to rows already written.
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
		// A recent iPad claims to be a Macintosh and nothing contradicts it. Reported as macOS, not
		// guessed: a wrong specific answer reads as fact to somebody deciding about their password.
		return "macOS"
	case strings.Contains(agent, "linux"):
		return "Linux"
	default:
		return ""
	}
}

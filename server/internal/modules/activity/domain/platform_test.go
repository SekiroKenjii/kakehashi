package domain

import "testing"

// The order of the cases inside PlatformOf is the whole implementation, so this table is mostly the
// strings a naive one misreads: an iPhone claims "like Mac OS X" and an Android claims "Linux".
func TestPlatformOfReadsTheFamilyOutOfAUserAgent(t *testing.T) {
	cases := []struct {
		name  string
		agent string
		want  string
	}{
		{
			"this app on windows",
			"Kakehashi/1.1.2 (Windows NT 10.0; Win64)",
			"Windows",
		},
		{
			"an iphone, which also claims to be a mac",
			"Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15",
			"iOS",
		},
		{
			"an ipad, same trap",
			"Mozilla/5.0 (iPad; CPU OS 17_0 like Mac OS X) AppleWebKit/605.1.15",
			"iOS",
		},
		{
			"an android, which also claims to be linux",
			"Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36",
			"Android",
		},
		{
			"an actual mac",
			"Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15",
			"macOS",
		},
		{
			"an actual linux",
			"Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36",
			"Linux",
		},
		{
			// Case is not something a user agent promises.
			"lower case still resolves",
			"kakehashi/1.1.2 (windows nt 10.0)",
			"Windows",
		},
		{
			// Empty rather than a guess: the reader is deciding whether to change their password,
			// and a made-up specific answer reads as a fact.
			"nothing recognisable",
			"some-internal-tool/3",
			"",
		},
		{
			"nothing at all",
			"",
			"",
		},
	}

	for _, c := range cases {
		t.Run(c.name, func(t *testing.T) {
			if got := PlatformOf(c.agent); got != c.want {
				t.Errorf("PlatformOf(%q) = %q, want %q", c.agent, got, c.want)
			}
		})
	}
}

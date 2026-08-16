package tui

import "testing"

func TestDefaultTitleSpacesTheWords(t *testing.T) {
	cases := map[string]string{
		"OrderDesk":  "Order Desk",
		"APIGateway": "API Gateway",
		"Kakehashi":  "Kakehashi",
		"App2Go":     "App2 Go",
		"":           "",
	}
	for name, want := range cases {
		if got := defaultTitle(name); got != want {
			t.Errorf("defaultTitle(%q) = %q, want %q", name, got, want)
		}
	}
}

func TestPascalDropsSeparators(t *testing.T) {
	cases := map[string]string{
		"order-desk": "OrderDesk",
		"order_desk": "OrderDesk",
		"order desk": "OrderDesk",
		"orderdesk":  "Orderdesk",
		"OrderDesk":  "OrderDesk",
	}
	for in, want := range cases {
		if got := pascal(in); got != want {
			t.Errorf("pascal(%q) = %q, want %q", in, got, want)
		}
	}
}

func TestSlugifyKeepsOnlyWhatAModulePathHolds(t *testing.T) {
	cases := map[string]string{
		"Thuong Vo":  "thuong-vo",
		"O'Brien":    "o-brien",
		"  ":         "",
		"already-ok": "already-ok",
	}
	for in, want := range cases {
		if got := slugify(in); got != want {
			t.Errorf("slugify(%q) = %q, want %q", in, got, want)
		}
	}
}

func TestRemoteOwnerReadsBothSpellingsGitWrites(t *testing.T) {
	cases := map[string]string{
		"https://github.com/SekiroKenjii/kakehashi.git": "github.com/SekiroKenjii",
		"https://github.com/SekiroKenjii/kakehashi":     "github.com/SekiroKenjii",
		"git@github.com:SekiroKenjii/kakehashi.git":     "github.com/SekiroKenjii",
		"ssh://git@example.com/team/thing.git":          "example.com/team",
		// Not a remote this can read an owner out of, which is not an error: the next default down
		// answers instead.
		"":                        "",
		"github.com/kakehashi":    "",
		"https://github.com/solo": "",
	}
	for remote, want := range cases {
		if got := remoteOwner(remote); got != want {
			t.Errorf("remoteOwner(%q) = %q, want %q", remote, got, want)
		}
	}
}

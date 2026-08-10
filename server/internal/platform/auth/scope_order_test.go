package auth_test

import (
	"slices"
	"testing"

	"github.com/SekiroKenjii/kakehashi/server/internal/platform/auth"
)

// The trap this file exists to keep shut.
//
// The store used to fold a caller's scopes across roles with MAX(rp.Scope) over an nvarchar column,
// under a comment claiming the scopes "sort the way they rank". They do not, and the comment was
// asserted nowhere: a test that looked like it checked the claim was checking auth.Widest, which
// ranks correctly in Go. An account holding one role at 'all' and another at 'team' therefore
// resolved to 'team' — the narrower of the two, which is the opposite of what widening means.
//
// The fix folds on an explicit rank in SQL. This test is what stops somebody reading the three
// values, noticing they are strings, and putting MAX back.

func TestTheScopeNamesDoNotSortTheWayTheyRank(t *testing.T) {
	byRank := []auth.Scope{auth.ScopeOwn, auth.ScopeTeam, auth.ScopeAll}

	alphabetical := slices.Clone(byRank)
	slices.Sort(alphabetical)

	if slices.Equal(byRank, alphabetical) {
		t.Fatal(
			"the scope names now sort in rank order, so the reason this test exists has gone; " +
				"check whether a scope was renamed and whether anything still relies on the two " +
				"orders differing")
	}

	// Concretely: the alphabetically largest is the NARROWEST, which is what made MAX() wrong.
	widestByName := alphabetical[len(alphabetical)-1]
	if widestByName != auth.ScopeTeam {
		t.Errorf("alphabetically largest scope is %q, expected %q", widestByName, auth.ScopeTeam)
	}
	if auth.Widest(auth.ScopeAll, auth.ScopeTeam) != auth.ScopeAll {
		t.Error("Widest no longer prefers all over team")
	}
}

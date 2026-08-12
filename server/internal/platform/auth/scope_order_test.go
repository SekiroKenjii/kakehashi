package auth_test

import (
	"slices"
	"testing"

	"github.com/SekiroKenjii/kakehashi/server/internal/platform/auth"
)

// The store used to fold a caller's scopes across roles with MAX(rp.Scope) over an nvarchar column,
// on the belief that the scope names sort the way they rank. They do not: an account holding one
// role at 'all' and another at 'team' resolved to 'team', the narrower of the two.
//
// The fix folds on an explicit rank in SQL. This test is what stops somebody noticing the three
// values are strings and putting MAX back.

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

	// The alphabetically largest is the narrowest, which is what made MAX() wrong.
	widestByName := alphabetical[len(alphabetical)-1]
	if widestByName != auth.ScopeTeam {
		t.Errorf("alphabetically largest scope is %q, expected %q", widestByName, auth.ScopeTeam)
	}
	if auth.Widest(auth.ScopeAll, auth.ScopeTeam) != auth.ScopeAll {
		t.Error("Widest no longer prefers all over team")
	}
}

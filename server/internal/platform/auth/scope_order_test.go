package auth_test

import (
	"slices"
	"testing"

	"__GO_MODULE__/server/internal/platform/auth"
)

// The trap this file exists to keep shut.
//
// The scope names do not sort the way they rank: alphabetically the largest is 'team', the
// NARROWEST. The store therefore folds a caller's scopes across roles on an explicit rank in SQL —
// never MAX() over the string column, which resolves 'all'+'team' to 'team', the opposite of
// widening. History of that defect: docs/adr/0005-scope-order-is-not-string-order.md.
//
// This test is what stops somebody reading the three values, noticing they are strings, and
// putting MAX back.

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

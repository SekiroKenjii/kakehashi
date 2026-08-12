package auth

import (
	"context"
	"testing"
)

func TestWidestNeverNarrows(t *testing.T) {
	// Two roles combine to the wider grant, so gaining a role can never take access away.
	cases := []struct {
		a, b, want Scope
	}{
		{ScopeOwn, ScopeAll, ScopeAll},
		{ScopeAll, ScopeOwn, ScopeAll},
		{ScopeOwn, ScopeTeam, ScopeTeam},
		{ScopeTeam, ScopeOwn, ScopeTeam},
		{ScopeNone, ScopeOwn, ScopeOwn},
		{ScopeOwn, ScopeNone, ScopeOwn},
		{ScopeAll, ScopeAll, ScopeAll},
		// An unknown scope ranks below everything, so it narrows rather than silently granting
		// more than it should.
		{Scope("galaxy"), ScopeOwn, ScopeOwn},
	}

	for _, c := range cases {
		if got := Widest(c.a, c.b); got != c.want {
			t.Errorf("Widest(%q, %q) = %q, want %q", c.a, c.b, got, c.want)
		}
	}
}

func TestGrantsTreatsAMissingKeyAsNoGrant(t *testing.T) {
	grants := Grants{"users.manage": ScopeAll}

	if grants.Scope("nothing.here") != ScopeNone {
		t.Error("a permission nobody granted must read as ScopeNone")
	}
	if grants.Allows("nothing.here") {
		t.Error("a permission nobody granted must not be allowed")
	}
	if !grants.Allows("users.manage") {
		t.Error("a granted permission must be allowed")
	}
}

func TestNilGrantsAreSafeToAsk(t *testing.T) {
	// An ungated route, or a server with no authorization module mounted, leaves nil on the
	// context. Asking must answer "no grant" rather than panic.
	var grants Grants

	if grants.Scope("users.manage") != ScopeNone || grants.Allows("users.manage") {
		t.Error("nil Grants must read as no grant")
	}
	if ScopeOf(context.Background(), "users.manage") != ScopeNone {
		t.Error("a context with no grants must read as no grant")
	}
}

func TestScopeOfReadsWhatTheGatePutOnTheContext(t *testing.T) {
	ctx := WithGrants(context.Background(), Grants{"notes.read": ScopeTeam})

	if got := ScopeOf(ctx, "notes.read"); got != ScopeTeam {
		t.Errorf("ScopeOf = %q, want %q", got, ScopeTeam)
	}
}

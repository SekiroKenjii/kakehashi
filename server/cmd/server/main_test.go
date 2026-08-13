package main

import (
	"slices"
	"testing"

	navigationapi "github.com/SekiroKenjii/kakehashi/server/internal/modules/navigation/api"
)

// What the composition root claims has to agree with what the modules declare, and nothing else
// checks it. These tests need no database and no boot, which is the point: a guarantee that only
// runs where there is a SQL Server is not a guarantee.

func mountedIDs() []string {
	mods := modules()

	out := make([]string, len(mods))
	for i, m := range mods {
		out[i] = m.ID()
	}
	return out
}

func TestNoTwoModulesClaimTheSameID(t *testing.T) {
	// The kernel panics on this at boot. Catching it here says which two, without a database.
	seen := make(map[string]struct{})
	for _, id := range mountedIDs() {
		if _, dup := seen[id]; dup {
			t.Errorf("two modules claim the id %q", id)
		}
		seen[id] = struct{}{}
	}
}

func TestEveryUnprotectedRouteModuleIsMounted(t *testing.T) {
	// A typo fails in the worst direction: the module it meant to name is refused at boot, and the
	// first symptom is a server that will not start.
	mounted := mountedIDs()
	for _, id := range unprotectedRouteModules {
		if !slices.Contains(mounted, id) {
			t.Errorf("unprotectedRouteModules names %q, which is not mounted", id)
		}
	}
}

func TestThePrerequisitesOfTheCheckAreExempt(t *testing.T) {
	// Not a policy choice: the check depends on these four.
	// docs/adr/0001-per-route-permission-policy.md names what each one would break.
	for _, id := range []string{"health", "account", "authz", "navigation"} {
		if !slices.Contains(unprotectedRouteModules, id) {
			t.Errorf("%q must be able to serve an unprotected route", id)
		}
	}
}

func TestTheFeatureModulesAreNotExempt(t *testing.T) {
	// The direction that matters: a newly mounted module cannot serve an unprotected route unless
	// somebody exempts it here, which turns "we forgot" into a failed boot rather than an open door.
	for _, id := range []string{"notes", "activity"} {
		if slices.Contains(unprotectedRouteModules, id) {
			t.Errorf("%q may serve an unprotected route; it should not be able to", id)
		}
	}
}

func TestEveryScreenIsReachableBySomebody(t *testing.T) {
	// A destination naming no permission falls back to <id>.access, which no route on an exempt
	// module checks — so the screen is disabled for everybody. The kernel refuses this at boot too.
	for _, m := range modules() {
		contributor, ok := m.(navigationapi.Contributor)
		if !ok {
			continue
		}

		for _, d := range contributor.NavigationDestinations() {
			if d.Permission != "" {
				continue
			}
			if slices.Contains(unprotectedRouteModules, m.ID()) {
				t.Errorf(
					"%q declares screen %q with no permission, but %q serves unprotected routes "+
						"so nothing grants its access permission",
					m.ID(), d.ID, m.ID())
			}
		}
	}
}

func TestNoTwoModulesClaimTheSameScreen(t *testing.T) {
	owner := make(map[string]string)
	for _, m := range modules() {
		contributor, ok := m.(navigationapi.Contributor)
		if !ok {
			continue
		}

		for _, d := range contributor.NavigationDestinations() {
			if first, dup := owner[d.ID]; dup {
				t.Errorf("screen %q is declared by both %q and %q", d.ID, first, m.ID())
			}
			owner[d.ID] = m.ID()
		}
	}
}

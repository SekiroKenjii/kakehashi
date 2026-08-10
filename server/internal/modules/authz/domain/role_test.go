package domain

import (
	"testing"

	"github.com/SekiroKenjii/kakehashi/server/internal/platform/errs"
)

func TestNewRoleTrimsAndRejectsWhatCannotBeShown(t *testing.T) {
	role, err := NewRole("role-1", "  Admin  ", "  Full system access  ", true)
	if err != nil {
		t.Fatalf("NewRole returned an error: %v", err)
	}
	if role.Name != "Admin" || role.Description != "Full system access" {
		t.Errorf("role = %+v, want the trimmed values", role)
	}

	for _, c := range []struct{ name, id, roleName string }{
		{"no id", "", "Admin"},
		{"no name", "role-1", ""},
		{"blank name", "role-1", "   "},
	} {
		t.Run(c.name, func(t *testing.T) {
			if _, err := NewRole(c.id, c.roleName, "", false); errs.KindOf(err) != errs.Invalid {
				t.Errorf("kind = %v, want %v", errs.KindOf(err), errs.Invalid)
			}
		})
	}
}

func TestGrantReplacesRatherThanAccumulates(t *testing.T) {
	// A role holding one permission at two scopes has no answer to "how far does this reach".
	role, _ := NewRole("role-1", "Admin", "", false)

	if err := role.Grant("notes.read", "own"); err != nil {
		t.Fatalf("Grant returned an error: %v", err)
	}
	if err := role.Grant("notes.read", "all"); err != nil {
		t.Fatalf("Grant returned an error: %v", err)
	}

	if len(role.Grants) != 1 {
		t.Fatalf("role holds %d grants, want 1", len(role.Grants))
	}
	if role.Grants["notes.read"] != "all" {
		t.Errorf("scope = %q, want the re-granted one", role.Grants["notes.read"])
	}
}

func TestGrantRejectsAScopeThisBuildDoesNotUnderstand(t *testing.T) {
	// Validated at the door: an unrecognised scope in the database is a grant whose reach nobody
	// can state, read long after whoever typed it has gone.
	role, _ := NewRole("role-1", "Admin", "", false)

	err := role.Grant("notes.read", "galaxy")

	if errs.KindOf(err) != errs.Invalid {
		t.Errorf("kind = %v, want %v", errs.KindOf(err), errs.Invalid)
	}
	if len(role.Grants) != 0 {
		t.Error("a rejected grant was stored anyway")
	}
}

func TestRevokeIsIdempotent(t *testing.T) {
	role, _ := NewRole("role-1", "Admin", "", false)
	_ = role.Grant("notes.read", "all")

	role.Revoke("notes.read")
	role.Revoke("notes.read")

	if len(role.Grants) != 0 {
		t.Errorf("role holds %d grants, want none", len(role.Grants))
	}
}

func TestASystemRoleCannotBeDeleted(t *testing.T) {
	// A deployment that deleted its admin role has no way back in.
	system, _ := NewRole("role-1", "Admin", "", true)
	custom, _ := NewRole("role-2", "Auditor", "", false)

	if err := system.CanDelete(); errs.KindOf(err) != errs.Invalid {
		t.Errorf("kind = %v, want %v", errs.KindOf(err), errs.Invalid)
	}
	// The refusal has to say why, or the screen shows a dead button with no explanation.
	if err := system.CanDelete(); err == nil || !contains(err.Error(), "Admin") {
		t.Errorf("the refusal does not name the role: %v", err)
	}
	if err := custom.CanDelete(); err != nil {
		t.Errorf("a custom role refused deletion: %v", err)
	}
}

func contains(haystack, needle string) bool {
	return len(haystack) >= len(needle) && (haystack == needle ||
		len(needle) == 0 || indexOf(haystack, needle) >= 0)
}

func indexOf(haystack, needle string) int {
	for i := 0; i+len(needle) <= len(haystack); i++ {
		if haystack[i:i+len(needle)] == needle {
			return i
		}
	}
	return -1
}

func TestRename_SystemRoleKeepsItsNameButNotItsDescription(t *testing.T) {
	role, _ := NewRole("id-1", "Admin", "Full system access", true)

	if err := role.Rename("Root", "x"); err == nil {
		t.Fatal("renaming a system role should be refused")
	}
	if err := role.Rename("Admin", "Everything, everywhere"); err != nil {
		t.Fatalf("editing a system role's description should be allowed: %v", err)
	}
	if role.Description != "Everything, everywhere" {
		t.Fatalf("description = %q", role.Description)
	}
}

func TestRename_OrdinaryRoleMayChangeBoth(t *testing.T) {
	role, _ := NewRole("id-1", "QA", "", false)

	if err := role.Rename("  QA Engineer  ", " tests things "); err != nil {
		t.Fatalf("Rename: %v", err)
	}
	if role.Name != "QA Engineer" || role.Description != "tests things" {
		t.Fatalf("got %q / %q", role.Name, role.Description)
	}
	if err := role.Rename("", ""); err == nil {
		t.Fatal("an empty name should be refused")
	}
}

func TestGrant_RevokingManageRolesIsAnOrdinaryDomainOperation(t *testing.T) {
	// The domain has no opinion about self-lockout: whether removing roles.manage is allowed
	// depends on who is asking, which is a service question. This pins that the aggregate itself
	// stays neutral, so the guard is not accidentally duplicated in two places.
	role, _ := NewRole("id-1", "Admin", "", true)
	if err := role.Grant("roles.manage", ScopeAll); err != nil {
		t.Fatalf("Grant: %v", err)
	}

	role.Revoke("roles.manage")

	if _, held := role.Grants["roles.manage"]; held {
		t.Fatal("Revoke should remove the grant")
	}
}

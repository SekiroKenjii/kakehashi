package main

import "testing"

// mod stands in for the real module path. Using a fake one keeps the tests honest: nothing here
// depends on the repository actually containing the packages being described.
const mod = "example.com/app"

// TestCheck covers every rule in both directions. A linter that only has tests for the things it
// rejects will happily reject everything.
func TestCheck(t *testing.T) {
	tests := []struct {
		name    string
		from    string
		to      string
		blocked bool
	}{
		// Rule 1 — cross-module reach.
		{
			name: "module reaches another module through its api",
			from: mod + "/internal/modules/notes/rpc",
			to:   mod + "/internal/modules/account/api",
		},
		{
			name:    "module reaches another module's service",
			from:    mod + "/internal/modules/notes/rpc",
			to:      mod + "/internal/modules/account/service",
			blocked: true,
		},
		{
			name: "module reaches its own internals",
			from: mod + "/internal/modules/notes/store",
			to:   mod + "/internal/modules/notes/domain",
		},

		// Rule 2 — contracts must not reference each other.
		{
			name:    "api imports another module's api",
			from:    mod + "/internal/modules/notes/api",
			to:      mod + "/internal/modules/account/api",
			blocked: true,
		},

		// Rule 3 — dependencies point inward.
		{
			name:    "platform imports a module",
			from:    mod + "/internal/platform/database",
			to:      mod + "/internal/modules/notes/api",
			blocked: true,
		},

		// Rule 4 — only cmd mounts modules.
		{
			name:    "kernel imports a module",
			from:    mod + "/internal/app",
			to:      mod + "/internal/modules/notes",
			blocked: true,
		},
		{
			name: "cmd imports a module",
			from: mod + "/cmd/server",
			to:   mod + "/internal/modules/notes",
		},

		// Rule 5 — persistence belongs to store/.
		{
			name: "store imports the sql database package",
			from: mod + "/internal/modules/notes/store",
			to:   mod + "/internal/platform/database",
		},
		{
			name: "a store sub-package still counts as store",
			from: mod + "/internal/modules/notes/store/mssql",
			to:   mod + "/internal/platform/database",
		},
		{
			name:    "service imports the sql database package",
			from:    mod + "/internal/modules/notes/service",
			to:      mod + "/internal/platform/database",
			blocked: true,
		},
		{
			name:    "module root imports mongo",
			from:    mod + "/internal/modules/activity",
			to:      mod + "/internal/platform/mongodb",
			blocked: true,
		},
		{
			name: "the kernel may import the database packages",
			from: mod + "/internal/app",
			to:   mod + "/internal/platform/database",
		},

		// Rule 6 — generated code stops at rpc/.
		{
			name: "rpc imports generated code",
			from: mod + "/internal/modules/notes/rpc",
			to:   mod + "/internal/gen/kakehashi/notes/v1",
		},
		{
			name:    "service imports generated code",
			from:    mod + "/internal/modules/notes/service",
			to:      mod + "/internal/gen/kakehashi/notes/v1",
			blocked: true,
		},
		{
			name:    "module root imports the generated connect package",
			from:    mod + "/internal/modules/notes",
			to:      mod + "/internal/gen/kakehashi/notes/v1/notesv1connect",
			blocked: true,
		},
		{
			// protoc-gen-connect-go emits a package that imports its sibling message package.
			// Nobody wrote that edge and nobody can remove it.
			name: "generated code imports generated code",
			from: mod + "/internal/gen/kakehashi/notes/v1/notesv1connect",
			to:   mod + "/internal/gen/kakehashi/notes/v1",
		},

		// Rule 7 — one module issues tokens.
		{
			name: "identity imports an oidc library",
			from: mod + "/internal/modules/account/rpc",
			to:   "github.com/zitadel/oidc/v3/pkg/op",
		},
		{
			name:    "another module imports an oidc library",
			from:    mod + "/internal/modules/notes/service",
			to:      "github.com/zitadel/oidc/v3/pkg/op",
			blocked: true,
		},
		{
			name:    "the kernel imports a jwt library",
			from:    mod + "/internal/app",
			to:      "github.com/golang-jwt/jwt/v5",
			blocked: true,
		},

		// Things that must stay legal, or the codebase cannot be written at all.
		{
			name: "module uses the platform",
			from: mod + "/internal/modules/notes/service",
			to:   mod + "/internal/platform/errs",
		},
		{
			name: "module uses the kernel",
			from: mod + "/internal/modules/notes",
			to:   mod + "/internal/app",
		},
		{
			name: "anything uses the standard library",
			from: mod + "/internal/modules/notes/domain",
			to:   "strings",
		},
	}

	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			got := check(mod, []pkg{{ImportPath: tt.from, Imports: []string{tt.to}}})

			if tt.blocked && len(got) == 0 {
				t.Fatalf("expected %s -> %s to be rejected, but it was allowed", tt.from, tt.to)
			}
			if !tt.blocked && len(got) != 0 {
				t.Fatalf("expected %s -> %s to be allowed, but got: %s",
					tt.from, tt.to, got[0].Reason)
			}
		})
	}
}

// TestCheckReportsEveryEdge guards against a rule that returns after the first violation: a linter
// that reports one problem per run turns a ten-minute fix into ten CI cycles.
func TestCheckReportsEveryEdge(t *testing.T) {
	got := check(mod, []pkg{
		{
			ImportPath: mod + "/internal/modules/notes/service",
			Imports: []string{
				mod + "/internal/platform/database",
				mod + "/internal/gen/kakehashi/notes/v1",
				mod + "/internal/modules/account/store",
			},
		},
	})

	if len(got) != 3 {
		t.Fatalf("expected 3 violations, got %d: %+v", len(got), got)
	}
}

func TestLayerOf(t *testing.T) {
	modulesPrefix := mod + "/internal/modules/"

	tests := []struct {
		path   string
		module string
		want   string
	}{
		{mod + "/internal/modules/notes", "notes", ""},
		{mod + "/internal/modules/notes/api", "notes", "api"},
		{mod + "/internal/modules/notes/store/mssql", "notes", "store"},
		{mod + "/internal/platform/errs", "", ""},
	}

	for _, tt := range tests {
		if got := layerOf(tt.path, modulesPrefix, tt.module); got != tt.want {
			t.Errorf("layerOf(%q) = %q, want %q", tt.path, got, tt.want)
		}
	}
}

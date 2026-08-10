// Command archlint enforces the module boundaries this codebase is built on.
//
// A modular monolith only stays modular if something checks. Left to code review alone, one "just
// this once" import from a handler straight into another module's service is all it takes, and a
// year later the modules are a single tangle with directories between them.
//
// So this runs in CI, and in `make lint`. It reads the import graph with `go list` and fails on any
// edge that breaks a rule below.
//
// # The rules
//
//  1. A module may not import another module's internals. Only its api package.
//
//     internal/modules/notes/rpc  ->  internal/modules/account/api      allowed
//     internal/modules/notes/rpc  ->  internal/modules/account/service  rejected
//
//     This is the rule the whole architecture rests on. The api package is a promise; everything
//     behind it is free to change.
//
//  2. An api package may not import another module at all, not even another api. Contracts that
//     reference each other are not contracts, they are a cycle waiting to be discovered.
//
//  3. The platform may not import a module. Dependencies point inward: modules know about the
//     platform, the platform knows nothing about them.
//
//  4. The kernel (internal/app) may not import a module. Only cmd/ may, and only to mount them.
//
//  5. Inside a module, only store/ may import the database packages. Persistence is one layer's
//     job; a service that reaches for a connection has stopped orchestrating and started querying,
//     and the tests that used to run without a database no longer do.
//
//  6. Only rpc/ may import the generated protobuf code. Generated types are the wire's shape, not
//     the module's. Let them into domain/ or service/ and a change to the schema becomes a change
//     to the business rules, which is exactly the coupling the api package exists to prevent.
//
//  7. Only the account module may import an OpenID Connect library. Token issuing lives in one
//     place or it lives in several, and the second one is discovered during an incident.
//
// # Adding a rule
//
// Rules are data, in the check function. If your project grows a convention worth keeping
// ("nothing outside store/ may import encoding/csv", say), add it there rather than trusting
// everyone to remember.
package main

import (
	"encoding/json"
	"fmt"
	"os"
	"os/exec"
	"sort"
	"strings"
)

// oidcLibraries are the import prefixes rule 7 fences off. Add to it rather than relaxing the rule.
var oidcLibraries = []string{
	"github.com/zitadel/oidc/",
	"github.com/ory/fosite",
	"github.com/go-jose/go-jose/",
	"github.com/golang-jwt/jwt/",
}

// accountModule is the one module allowed to hold them.
const accountModule = "account"

// pkg is the slice of `go list -json` output we care about.
type pkg struct {
	ImportPath string
	Imports    []string
}

// violation is one rejected import edge.
type violation struct {
	From   string
	To     string
	Reason string
}

func main() {
	module, err := modulePath()
	if err != nil {
		fatal(err)
	}

	pkgs, err := listPackages()
	if err != nil {
		fatal(err)
	}

	violations := check(module, pkgs)
	if len(violations) == 0 {
		fmt.Printf("archlint: %d packages, no boundary violations\n", len(pkgs))
		return
	}

	sort.Slice(violations, func(i, j int) bool {
		if violations[i].From != violations[j].From {
			return violations[i].From < violations[j].From
		}
		return violations[i].To < violations[j].To
	})

	fmt.Fprintf(os.Stderr, "archlint: %d boundary violation(s)\n\n", len(violations))
	for _, v := range violations {
		fmt.Fprintf(os.Stderr, "  %s\n      imports %s\n      %s\n\n",
			trim(module, v.From), trim(module, v.To), v.Reason)
	}
	os.Exit(1)
}

func check(module string, pkgs []pkg) []violation {
	var out []violation

	modulesPrefix := module + "/internal/modules/"
	platformPrefix := module + "/internal/platform/"
	kernelPrefix := module + "/internal/app"
	genPrefix := module + "/internal/gen"

	for _, p := range pkgs {
		// Generated code is machine-written and sits outside the architecture: the Connect package
		// imports its sibling message package because the generator says so, and no edit anyone
		// could make would answer a complaint about it. Skip it as a source; it is still checked
		// as a target, which is what rule 6 is for.
		if isUnder(p.ImportPath, genPrefix) {
			continue
		}

		fromModule := moduleOf(p.ImportPath, modulesPrefix)
		fromLayer := layerOf(p.ImportPath, modulesPrefix, fromModule)
		fromIsPlatform := strings.HasPrefix(p.ImportPath, platformPrefix)
		fromIsKernel := p.ImportPath == kernelPrefix ||
			strings.HasPrefix(p.ImportPath, kernelPrefix+"/")

		for _, imp := range p.Imports {
			// Rule 6. Checked first because the generated code lives under internal/, so the
			// module-boundary rules below would never see it.
			if isUnder(imp, genPrefix) && fromLayer != "rpc" {
				out = append(out, violation{p.ImportPath, imp,
					"only a module's rpc package may import generated protobuf code: " +
						"generated types are the wire's shape, not the module's"})
				continue
			}

			// Rule 7.
			if isOIDCLibrary(imp) && fromModule != accountModule {
				out = append(out, violation{p.ImportPath, imp,
					fmt.Sprintf("only the %q module may import an OpenID Connect library",
						accountModule)})
				continue
			}

			// Rule 5. Scoped to modules: the kernel opens the connections and hands them out, so
			// it has to import both, and the platform packages import each other freely.
			if isStorage(imp, platformPrefix) && fromModule != "" && fromLayer != "store" {
				out = append(out, violation{p.ImportPath, imp,
					fmt.Sprintf("only %s/store may import the database packages: "+
						"persistence belongs to one layer", fromModule)})
				continue
			}

			toModule := moduleOf(imp, modulesPrefix)
			if toModule == "" {
				// Not an import of a feature module. Nothing here forbids a module from using the
				// platform or the standard library.
				continue
			}
			toIsAPI := layerOf(imp, modulesPrefix, toModule) == "api"

			switch {
			case fromIsPlatform:
				out = append(out, violation{p.ImportPath, imp,
					"the platform must not depend on a module: dependencies point inward"})

			case fromIsKernel:
				out = append(out, violation{p.ImportPath, imp,
					"the kernel must not depend on a module: only cmd/ may mount modules"})

			case fromLayer == "api" && toModule != fromModule:
				out = append(out, violation{p.ImportPath, imp,
					"an api package must not depend on another module: " +
						"contracts that reference each other are a cycle"})

			case fromModule != "" && toModule != fromModule && !toIsAPI:
				out = append(out, violation{p.ImportPath, imp,
					fmt.Sprintf("module %q may only reach module %q through its api package",
						fromModule, toModule)})
			}
		}
	}

	return out
}

// moduleOf returns the feature module a package belongs to, or "" when the package is not part of
// one.
func moduleOf(importPath, modulesPrefix string) string {
	if !strings.HasPrefix(importPath, modulesPrefix) {
		return ""
	}
	rest := strings.TrimPrefix(importPath, modulesPrefix)
	if i := strings.Index(rest, "/"); i >= 0 {
		return rest[:i]
	}
	return rest
}

// layerOf returns the layer a package sits in within its module — "api", "domain", "store",
// "service", "rpc" — or "" for the module root (module.go) and for packages outside any module.
//
// Sub-packages count as their layer: internal/modules/notes/store/mssql is still store.
func layerOf(importPath, modulesPrefix, module string) string {
	if module == "" {
		return ""
	}
	rest := strings.TrimPrefix(importPath, modulesPrefix+module)
	rest = strings.TrimPrefix(rest, "/")
	if rest == "" {
		return ""
	}
	if i := strings.Index(rest, "/"); i >= 0 {
		return rest[:i]
	}
	return rest
}

// isUnder reports whether importPath is prefix itself or something below it.
func isUnder(importPath, prefix string) bool {
	return importPath == prefix || strings.HasPrefix(importPath, prefix+"/")
}

// isStorage reports whether importPath is one of the platform's persistence packages.
func isStorage(importPath, platformPrefix string) bool {
	return isUnder(importPath, platformPrefix+"database") ||
		isUnder(importPath, platformPrefix+"mongodb")
}

func isOIDCLibrary(importPath string) bool {
	for _, lib := range oidcLibraries {
		if importPath == strings.TrimSuffix(lib, "/") || strings.HasPrefix(importPath, lib) {
			return true
		}
	}
	return false
}

func listPackages() ([]pkg, error) {
	// -deps is deliberately absent: we only want this module's own packages, not the transitive
	// closure of the standard library and every dependency.
	cmd := exec.Command("go", "list", "-json", "./...")
	cmd.Stderr = os.Stderr

	out, err := cmd.Output()
	if err != nil {
		return nil, fmt.Errorf("go list: %w", err)
	}

	// `go list -json` emits a stream of concatenated objects, not an array.
	var pkgs []pkg
	dec := json.NewDecoder(strings.NewReader(string(out)))
	for dec.More() {
		var p pkg
		if err := dec.Decode(&p); err != nil {
			return nil, fmt.Errorf("parse go list output: %w", err)
		}
		pkgs = append(pkgs, p)
	}
	return pkgs, nil
}

func modulePath() (string, error) {
	out, err := exec.Command("go", "list", "-m").Output()
	if err != nil {
		return "", fmt.Errorf("go list -m: %w", err)
	}
	return strings.TrimSpace(string(out)), nil
}

// trim shortens an import path for display: the module prefix is the same on every line and
// carries no information.
func trim(module, importPath string) string {
	return strings.TrimPrefix(strings.TrimPrefix(importPath, module), "/")
}

func fatal(err error) {
	fmt.Fprintln(os.Stderr, "archlint:", err)
	os.Exit(1)
}

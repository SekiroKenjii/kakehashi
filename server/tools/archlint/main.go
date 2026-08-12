// Command archlint enforces the module boundaries this codebase is built on.
//
// A modular monolith only stays modular if something checks: left to code review alone, one "just
// this once" import from a handler straight into another module's service is all it takes. So this
// runs in CI and in `make lint`, reading the import graph with `go list` and failing on any edge
// that breaks a rule.
//
//  1. A module may not import another module's internals, only its api package. This is the rule
//     the whole architecture rests on: the api package is a promise, and everything behind it is
//     free to change.
//
//  2. An api package may not import another module at all, not even another api. Contracts that
//     reference each other are not contracts, they are a cycle waiting to be discovered.
//
//  3. The platform may not import a module. Dependencies point inward.
//
//  4. The kernel (internal/app) may not import a module. Only cmd/ may, and only to mount them.
//
//  5. Inside a module, only store/ may import the database packages. A service that reaches for a
//     connection has stopped orchestrating and started querying, and the tests that used to run
//     without a database no longer do.
//
//  6. Only rpc/ may import the generated protobuf code. Let it into domain/ or service/ and a
//     change to the schema becomes a change to the business rules, which is the coupling the api
//     package exists to prevent.
//
//  7. Only the account module may import an OpenID Connect library. Token issuing lives in one
//     place or it lives in several, and the second one is discovered during an incident.
//
// Rules are data, in the check function. Add a new convention there rather than trusting everyone
// to remember it.
package main

import (
	"encoding/json"
	"fmt"
	"os"
	"os/exec"
	"sort"
	"strings"
)

// The import prefixes rule 7 fences off. Add to it rather than relaxing the rule.
var oidcLibraries = []string{
	"github.com/zitadel/oidc/",
	"github.com/ory/fosite",
	"github.com/go-jose/go-jose/",
	"github.com/golang-jwt/jwt/",
}

const accountModule = "account"

// pkg is the slice of `go list -json` output we care about.
type pkg struct {
	ImportPath string
	Imports    []string
}

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
		// Generated code sits outside the architecture: the Connect package imports its sibling
		// message package because the generator says so, and no edit anyone could make would
		// answer a complaint about it. Skipped as a source, still checked as a target by rule 6.
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
				// Nothing forbids a module from using the platform or the standard library.
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

// Returns "" when the package is not part of a feature module.
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

// Returns "" for the module root (module.go) and for packages outside any module. Sub-packages
// count as their layer: internal/modules/notes/store/mssql is still store.
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

// True for prefix itself, as well as anything below it.
func isUnder(importPath, prefix string) bool {
	return importPath == prefix || strings.HasPrefix(importPath, prefix+"/")
}

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

// The module prefix is the same on every line and carries no information.
func trim(module, importPath string) string {
	return strings.TrimPrefix(strings.TrimPrefix(importPath, module), "/")
}

func fatal(err error) {
	fmt.Fprintln(os.Stderr, "archlint:", err)
	os.Exit(1)
}

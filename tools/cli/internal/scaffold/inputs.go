package scaffold

import (
	"fmt"
	"regexp"
	"strings"
	"time"
)

// The sign-in modes --auth accepts. None is named so the refusal can say it is not built yet
// rather than that it is not a word.
const (
	AuthInApp   = "inapp"
	AuthBrowser = "browser"
	AuthNone    = "none"
)

// DefaultAccent is the template's own vermilion.
const DefaultAccent = "#E34234"

// Inputs is what a project is named and coloured. Every field ends up substituted into the tree,
// and the set is recorded in the manifest so a later upgrade can reproduce this scaffold.
type Inputs struct {
	AppName       string
	AppTitle      string
	RootNamespace string
	GoModule      string
	ProtoPackage  string
	Accent        string
	Author        string
	Year          string
	Auth          string
	WithExample   bool
}

// The rules are docs/pivot/02-PHASE-1-TEMPLATIZATION.md §1, and the rename scripts enforce the same
// ones. AppTitle and Author are free text and have no pattern.
var (
	appNamePattern       = regexp.MustCompile(`^[A-Z][A-Za-z0-9]{1,39}$`)
	goModulePattern      = regexp.MustCompile(`^[a-zA-Z0-9][a-zA-Z0-9._~/-]*[a-zA-Z0-9]$`)
	protoPackagePattern  = regexp.MustCompile(`^[a-z][a-z0-9_]*$`)
	rootNamespacePattern = regexp.MustCompile(`^[A-Z][A-Za-z0-9.]*$`)
	accentPattern        = regexp.MustCompile(`^#[0-9A-Fa-f]{6}$`)
	yearPattern          = regexp.MustCompile(`^[0-9]{4}$`)
)

// placeholderPattern is what the template spells a substitution site with, and what the self-check
// looks for afterwards.
var placeholderPattern = regexp.MustCompile(`__[A-Z][A-Z0-9_]*__`)

// markupPattern is what a free-text value may not contain. Substitution is literal, and the same
// value lands in a Go string literal, an XML attribute and a JSON string — each of which escapes
// these differently. Refusing is the only answer that is correct in all three.
var markupPattern = regexp.MustCompile(`["&<>\\]|[\x00-\x1f]`)

// Derive fills in every value that has a default in terms of another. It leaves Author alone: the
// default for that one is read out of git, which is the caller's business.
func (in *Inputs) Derive(now time.Time) {
	if in.AppTitle == "" {
		in.AppTitle = in.AppName
	}
	if in.RootNamespace == "" {
		in.RootNamespace = in.AppName
	}
	if in.ProtoPackage == "" {
		in.ProtoPackage = strings.ToLower(in.AppName)
	}
	if in.Accent == "" {
		in.Accent = DefaultAccent
	}
	if in.Author == "" {
		in.Author = in.AppName
	}
	if in.Year == "" {
		in.Year = fmt.Sprintf("%d", now.UTC().Year())
	}
	if in.Auth == "" {
		in.Auth = AuthInApp
	}
}

// Validate checks every input against its pattern. Derive runs first: these rules are written for
// derived values as much as for given ones.
func (in Inputs) Validate() error {
	for _, rule := range []struct {
		flag    string
		value   string
		pattern *regexp.Regexp
	}{
		{"--app-name", in.AppName, appNamePattern},
		{"--module", in.GoModule, goModulePattern},
		{"--proto-package", in.ProtoPackage, protoPackagePattern},
		{"--root-namespace", in.RootNamespace, rootNamespacePattern},
		{"--accent", in.Accent, accentPattern},
		{"--year", in.Year, yearPattern},
	} {
		if !rule.pattern.MatchString(rule.value) {
			return fmt.Errorf("%s must match %s, got %q", rule.flag, rule.pattern, rule.value)
		}
	}

	for _, text := range []struct {
		flag  string
		value string
	}{{"--title", in.AppTitle}, {"--author", in.Author}} {
		if strings.TrimSpace(text.value) == "" {
			return fmt.Errorf("%s must not be empty", text.flag)
		}
		if found := markupPattern.FindString(text.value); found != "" {
			return fmt.Errorf("%s may not contain %q: it is substituted into XML, JSON and source "+
				"code, which escape it three different ways", text.flag, found)
		}
	}
	switch in.Auth {
	case AuthInApp, AuthBrowser:
	case AuthNone:
		return fmt.Errorf("--auth none is not built yet: the template does not carry auth as a removable unit")
	default:
		return fmt.Errorf("--auth must be %s or %s, got %q", AuthInApp, AuthBrowser, in.Auth)
	}

	// A value shaped like a placeholder survives its own substitution and fails the self-check at
	// the end of a scaffold that has otherwise worked.
	for _, r := range in.replacements() {
		if placeholderPattern.MatchString(r.value) {
			return fmt.Errorf("%s may not contain a placeholder: %q", r.name, r.value)
		}
	}
	return nil
}

type replacement struct {
	name  string
	value string
}

// replacements is the substitution table, longest name first: __APP_NAME_LOWER__ starts with
// __APP_NAME_, so substituting the short one first would leave "OrderDeskLOWER__" behind.
func (in Inputs) replacements() []replacement {
	return []replacement{
		{"__APP_NAME_LOWER__", strings.ToLower(in.AppName)},
		{"__APP_NAME_UPPER__", strings.ToUpper(in.AppName)},
		{"__APP_NAME__", in.AppName},
		{"__APP_TITLE__", in.AppTitle},
		{"__ROOT_NAMESPACE__", in.RootNamespace},
		{"__PROTO_PACKAGE__", in.ProtoPackage},
		{"__GO_MODULE__", in.GoModule},
		{"__ACCENT__", in.Accent},
		{"__AUTHOR__", in.Author},
		{"__YEAR__", in.Year},
	}
}

// apply substitutes every placeholder in s.
func (in Inputs) apply(s string) string {
	for _, r := range in.replacements() {
		s = strings.ReplaceAll(s, r.name, r.value)
	}
	return s
}

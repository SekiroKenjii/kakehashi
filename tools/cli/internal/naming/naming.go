// Package naming derives every spelling of a module's name from its id. A generator that asks the
// caller for each spelling separately gets them inconsistent; asking for one and deriving the rest
// is what keeps a package name, a type name, a table name and a namespace segment in agreement.
package naming

import (
	"fmt"
	"regexp"
	"strings"
	"unicode"
)

// IDPattern is what a module id may be: lower case, no separators, short enough to read in a
// package path, a SQL schema name and a C# namespace segment.
var IDPattern = regexp.MustCompile(`^[a-z][a-z0-9]{1,29}$`)

// reserved is what a module may not be called. The first group would collide with a package the
// server already has, the second with a directory the layout gives another meaning.
var reserved = map[string]bool{
	"app": true, "platform": true, "gen": true, "api": true, "domain": true,
	"store": true, "service": true, "rpc": true, "internal": true, "cmd": true,
	"auth": true, "account": true, "navigation": true, "activity": true,
	"authz": true, "health": true, "test": true, "tests": true, "main": true,
}

// Names is one module's vocabulary, in every case and number the generated files need.
type Names struct {
	ID       string // orders — package, schema, proto directory, unit id
	Module   string // Orders — namespace segment, service type prefix
	Entity   string // Order  — the aggregate's type name
	Variable string // order  — a local holding one of them
	Icon     string // the navigation icon the server declares, a name from the client's vocabulary
	Glyph    string // what the client draws for it until somebody picks a better one
	Title    string // Orders — what the navigation pane shows
}

// New derives the vocabulary from an id, with an optional entity name for the words English does
// not inflect the way the rules below assume.
func New(id, entity, icon string) (Names, error) {
	if !IDPattern.MatchString(id) {
		return Names{}, fmt.Errorf("module id %q must match %s", id, IDPattern)
	}
	if reserved[id] {
		return Names{}, fmt.Errorf("module id %q is reserved", id)
	}

	if entity == "" {
		entity = pascal(singular(id))
	}
	if !entityPattern.MatchString(entity) {
		return Names{}, fmt.Errorf("entity %q must match %s", entity, entityPattern)
	}
	if icon == "" {
		icon = DefaultIcon
	}

	return Names{
		ID:       id,
		Module:   pascal(id),
		Entity:   entity,
		Variable: lowerFirst(entity),
		Icon:     icon,
		Glyph:    DefaultGlyph,
		Title:    pascal(id),
	}, nil
}

// DefaultIcon is what a generated module asks the pane to draw. The vocabulary belongs to the
// client (docs/adr/0013), and an unknown name falls back to a default glyph rather than failing,
// so the generator picks a name that vocabulary already has.
const DefaultIcon = "document"

// DefaultGlyph is what the client draws for a generated module. The client maps semantic names to
// glyphs and the pane is given a glyph directly, so this is the one place the two vocabularies are
// not resolved through each other — the generated module names the document icon in both.
const DefaultGlyph = `\uE8A5`

var entityPattern = regexp.MustCompile(`^[A-Z][A-Za-z0-9]{1,39}$`)

// singular strips the plural the id almost always is. English has more rules than these three, and
// --entity exists for the words that need them.
func singular(id string) string {
	switch {
	case strings.HasSuffix(id, "ies") && len(id) > 3:
		return strings.TrimSuffix(id, "ies") + "y"
	case strings.HasSuffix(id, "ses") || strings.HasSuffix(id, "xes") || strings.HasSuffix(id, "zes"):
		return strings.TrimSuffix(id, "es")
	case strings.HasSuffix(id, "s") && !strings.HasSuffix(id, "ss"):
		return strings.TrimSuffix(id, "s")
	default:
		return id
	}
}

func pascal(s string) string {
	if s == "" {
		return s
	}
	runes := []rune(s)
	runes[0] = unicode.ToUpper(runes[0])
	return string(runes)
}

func lowerFirst(s string) string {
	if s == "" {
		return s
	}
	runes := []rune(s)
	runes[0] = unicode.ToLower(runes[0])
	return string(runes)
}

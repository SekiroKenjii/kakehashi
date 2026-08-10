// Package domain holds the navigation module's types and the rules it enforces.
//
// No aggregate root, and that is a statement rather than an omission. A group and a placement
// defend no invariant across each other: moving an item is one row, renaming a heading is another,
// and neither can leave the other inconsistent — the database's own foreign key covers the one case
// that could (a placement pointing at a heading that is gone), by clearing it.
//
// What is here is the vocabulary and the two rules worth stating in one place: what makes a legal
// slug, and what makes a legal title.
package domain

import (
	"strings"
	"unicode"
	"unicode/utf16"

	"github.com/SekiroKenjii/kakehashi/server/internal/platform/errs"
)

// MaxTitle is the longest a heading or an override may be. It matches the column, so a title that
// fits here fits the database — the alternative is a truncation nobody asked for or a driver error
// with a message written for a DBA.
const MaxTitle = 64

// Group is one heading in the pane.
type Group struct {
	// ID is a slug. Stable, because a placement stores it: renaming a group's ID is a move of
	// everything under it, not an edit.
	ID    string
	Title string
	Order int

	// IsSystem marks a heading the product ships. Renamable, re-orderable, never deletable.
	IsSystem bool
}

// Placement is where one destination sits. The stored half of a destination — its declaration is
// the other half and lives in code.
type Placement struct {
	// DestinationID is what the declaration calls it.
	DestinationID string

	// ModuleID is denormalised from the declaration so an orphan row can still say which module it
	// came from. A row whose destination this build no longer has is the case the whole field
	// exists for: without it an orphan is an unexplained id.
	ModuleID string

	// GroupID is empty for ungrouped, which is also where the database puts a placement when the
	// group holding it is deleted.
	GroupID string

	// Title and Icon are overrides. Empty means "whatever the code says", so a page that gets
	// renamed carries the new name everywhere nobody deliberately overrode it.
	Title string
	Icon  string

	Order     int
	IsVisible bool
}

// NewGroup builds a heading, rejecting what could not be shown or referred to.
func NewGroup(id, title string, order int, isSystem bool) (Group, error) {
	title = strings.TrimSpace(title)
	if title == "" {
		return Group{}, errs.Invalidf("A heading needs a name.")
	}
	if utf16Len(title) > MaxTitle {
		return Group{}, errs.Invalidf("A heading's name cannot be longer than %d characters.", MaxTitle)
	}

	// An empty id is derived from the title rather than refused: somebody naming a heading should
	// not also have to invent the identifier it is stored under.
	id = strings.TrimSpace(id)
	if id == "" {
		id = Slug(title)
		if id == "" {
			// The derivation failed, which is a different problem from a bad identifier: the title
			// is readable but has no character a slug may contain — "日本語", "///". Saying so beats
			// reporting an empty identifier the person never typed.
			return Group{}, errs.Invalidf(
				"An identifier could not be derived from %q. Give the heading one, using lowercase "+
					"letters, digits and hyphens.", title)
		}
	}
	if err := ValidateSlug(id); err != nil {
		return Group{}, err
	}

	return Group{ID: id, Title: title, Order: order, IsSystem: isSystem}, nil
}

// ValidateSlug rejects anything that would not survive being a stable identifier.
//
// Lowercase, digits and hyphens. Narrow on purpose: this value ends up in a URL, a log line and a
// configuration file, and a heading called "Ops / Tools" would arrive at each of those places
// spelled differently.
func ValidateSlug(id string) error {
	if id == "" {
		return errs.Invalidf("A heading needs an identifier.")
	}
	if len(id) > MaxTitle {
		return errs.Invalidf("A heading's identifier cannot be longer than %d characters.", MaxTitle)
	}

	for _, r := range id {
		if r >= 'a' && r <= 'z' || r >= '0' && r <= '9' || r == '-' {
			continue
		}
		return errs.Invalidf(
			"A heading's identifier may only contain lowercase letters, digits and hyphens; "+
				"%q does not.", id)
	}
	return nil
}

// Slug derives an identifier from a title. Anything that is not a letter or a digit becomes a
// hyphen, and runs of them collapse.
func Slug(title string) string {
	var b strings.Builder
	lastHyphen := true

	for _, r := range strings.ToLower(title) {
		switch {
		case unicode.IsLetter(r) && r < unicode.MaxASCII || unicode.IsDigit(r) && r < unicode.MaxASCII:
			b.WriteRune(r)
			lastHyphen = false
		case !lastHyphen:
			b.WriteRune('-')
			lastHyphen = true
		}
	}

	return strings.Trim(b.String(), "-")
}

// NormaliseOverride trims an override and rejects one too long to store.
//
// Empty is a legal answer and means "clear it": there has to be a way back to what the code calls
// a page, or an override is a one-way door.
func NormaliseOverride(what, value string) (string, error) {
	value = strings.TrimSpace(value)
	if utf16Len(value) > MaxTitle {
		return "", errs.Invalidf("A %s cannot be longer than %d characters.", what, MaxTitle)
	}
	return value, nil
}

// utf16Len is how many units a string takes in an nvarchar column.
//
// Not the rune count, which is what these checks used. nvarchar(n) counts UTF-16 code units, and a
// character outside the Basic Multilingual Plane — an emoji, an old CJK ideograph — takes two of
// them. Counting runes let a value pass the domain and fail the INSERT, turning a message somebody
// could act on into an opaque 500 from the driver.
func utf16Len(s string) int {
	return len(utf16.Encode([]rune(s)))
}

// Package navigationapi is the navigation module's public contract.
//
// An ordinary cross-module contract, in the shape authzapi.Catalogue already uses: a module that
// owns a screen implements Contributor and says what that screen is. Nothing else has to know, and
// the navigation module never learns which modules exist by importing them.
//
// A destination is declared in code and placed in the database. The declaration says what exists
// and what protects it; the database says where it sits, in what order, under what label, and
// whether it is offered. Neither can do the other's job, which is the point: an administrator
// rearranging a pane cannot accidentally remove a permission check, and a deploy is not needed to
// rename a heading.
package navigationapi

// PermissionManageNavigation guards the whole layout surface.
//
// Its own permission rather than roles.manage: arranging a pane and granting access are different
// jobs, and someone trusted to tidy the navigation need not be trusted to hand out permissions.
const PermissionManageNavigation = "navigation.manage"

// Contributor is implemented by a module that owns a screen.
//
// It is how a module says "this destination exists, this is what it is called, and this is what
// protects it" without anything having to be listed twice. The two facts only the module can be
// sure of — its permission and whether the screen should be hidden rather than locked — stay in
// the module that owns them.
type Contributor interface {
	NavigationDestinations() []Destination
}

// Destination is one place a client can navigate to, as code declares it.
//
// The Default fields are seeds. Reconcile writes them the first time it sees a destination and
// never again, so a deployment starts arranged the way the product intends and stays arranged the
// way its administrator left it — an administrator's rearrangement outlives every restart.
type Destination struct {
	// ID is stable and unique across the build — "notes", "account.users". It is what the database
	// stores, so renaming one loses that destination's placement and starts it over from the
	// defaults below.
	//
	// It is not the module's ID. One module can own several destinations: the account module owns
	// both the Account page and the Users page, which is ordinary rather than exceptional.
	ID string

	// ModuleID is the module that owns it, and where its access permission comes from when
	// Permission is empty.
	//
	// A module does not set this: the navigation module stamps it from whichever module returned
	// the declaration, for the reason the kernel stamps Route.Module. It decides which permission
	// applies, and a value a module could choose for itself is a permission a module could grant
	// itself.
	ModuleID string

	DefaultTitle string

	// DefaultIcon is a semantic name — "note", "people" — never a glyph. Which glyph draws it is a
	// client's business.
	DefaultIcon string

	// DefaultGroup is the group slug to seed it into. Empty means ungrouped: above every heading.
	DefaultGroup string

	DefaultOrder int

	// Permission is what a caller needs before this destination is usable. Empty means the owning
	// module's .access — the ordinary case, and the reason most destinations name nothing here.
	//
	// In code, and only in code. It is enforced by the route gate from the same declaration, so
	// there is no write anywhere in this module that can change it. That independence is what makes
	// the layout surface safe to hand to an administrator.
	Permission string

	// HideWhenDenied removes the destination from a caller's pane instead of showing it disabled.
	//
	// Disabled is the default because a product having something this account has not been given is
	// worth being able to see and ask for. Hiding is for the destinations where the existence is
	// itself administrative — a user directory, a permissions matrix — and where a locked row on
	// every screen tells an ordinary account nothing it can act on.
	HideWhenDenied bool
}

// SystemGroup is a heading the product ships.
//
// Seeded by Reconcile, renamable and re-orderable afterwards, never deletable. A deployment that
// deleted the heading its administrative screens live under would have nowhere to put them.
type SystemGroup struct {
	ID    string
	Title string
	Order int
}

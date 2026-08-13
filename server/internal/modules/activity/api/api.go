// Package activityapi is the activity module's public contract.
//
// Other modules import this package and nothing else under internal/modules/activity/. Service has
// no Record method: entries are written by this module reacting to facts other modules publish.
// CanReport governs the one client-originated write path — the client chooses a kind from a closed
// list and never names an arbitrary one.
package activityapi

import (
	"context"
	"sort"
	"time"
)

// Feed entry kinds.
//
// These strings cross the wire and clients switch on them to choose a label and an icon; renaming
// one breaks deployed clients. Treat them as contract.
//
// They are declared here rather than imported from another module's api because an api package may
// not import another module; subscriptions.go maps the account module's vocabulary onto this one.
const (
	KindSignedIn  = "SignedIn"
	KindSignedOut = "SignedOut"

	// KindNewDeviceSignedIn is a sign-in from a device this account has not used before. It sits
	// in CategorySignIn; the client badges it.
	KindNewDeviceSignedIn = "NewDeviceSignedIn"

	// KindSessionRevoked is a session ended by a decision rather than by leaving, which is
	// KindSignedOut. Two kinds on purpose: docs/adr/0003-signedout-vs-sessionrevoked.md
	KindSessionRevoked = "SessionRevoked"

	// KindSessionRevokedByAdmin is somebody other than the account holder ending their session.
	// Its own kind rather than an attribute of KindSessionRevoked because clients pick a label and
	// an icon by kind.
	KindSessionRevokedByAdmin = "SessionRevokedByAdmin"

	// KindFailedSignIn only ever concerns an account that exists — see accountapi.FailedSignIn.
	KindFailedSignIn = "FailedSignIn"

	KindPasswordChanged = "PasswordChanged"

	// KindAppUpdated and KindThemeChanged are the two facts only the client can know: nothing on
	// this server observes which build somebody is running or what theme they set. They are the
	// entire list the client write path accepts — see ClientReportableKinds.
	KindAppUpdated   = "AppUpdated"
	KindThemeChanged = "ThemeChanged"
)

// clientReportable is the closed set of kinds a client may report about itself. The server never
// takes the client's word for the kind: an open set would let a compromised client write
// "SignedIn" into a security feed. Adding to this list is a security decision — ask first whether
// a compromised client could use the new kind to tell a lie a reader would act on.
var clientReportable = map[string]bool{
	KindAppUpdated:   true,
	KindThemeChanged: true,
}

// ClientReportableKinds lists what a client may report, sorted. For documentation and for tests.
func ClientReportableKinds() []string {
	kinds := make([]string, 0, len(clientReportable))
	for kind := range clientReportable {
		kinds = append(kinds, kind)
	}
	sort.Strings(kinds)
	return kinds
}

// CanReport answers whether a client may record this kind about itself.
func CanReport(kind string) bool {
	return clientReportable[kind]
}

// Feed categories: what a chip along the top of the feed filters by, and what the counts count.
//
// These strings cross the wire and clients switch on them; renaming one breaks deployed clients.
// The kind-to-category mapping lives here, beside the kinds, so the server's counts and the
// client's filters resolve to the same answer.
const (
	CategorySignIn   = "SignIn"
	CategorySecurity = "Security"

	// CategorySystem is what happened to the application rather than to the account.
	CategorySystem = "System"
)

// categories is the one table CategoryOf and KindsIn both read; the two questions are each other's
// inverse, and a pair that disagrees is a chip whose count does not match what it shows.
var categories = map[string]string{
	KindSignedIn:              CategorySignIn,
	KindSignedOut:             CategorySignIn,
	KindNewDeviceSignedIn:     CategorySignIn,
	KindSessionRevoked:        CategorySignIn,
	KindFailedSignIn:          CategorySecurity,
	KindPasswordChanged:       CategorySecurity,
	KindSessionRevokedByAdmin: CategorySecurity,
	KindAppUpdated:            CategorySystem,
	KindThemeChanged:          CategorySystem,
}

// CategoryOf answers which chip an entry belongs under. An unrecognised kind — newer than this
// build, or wrong — maps to Security so it surfaces where people look, not in an unfiltered
// bucket.
func CategoryOf(kind string) string {
	if category, ok := categories[kind]; ok {
		return category
	}
	return CategorySecurity
}

// KindsIn lists the kinds a category selects, sorted, or nil for a category this build does not
// know — which a caller reads as "do not filter" rather than "match nothing".
//
// Deliberately asymmetric with CategoryOf: a kind written by a newer build is counted under
// Security but not selected by the Security chip, because this build cannot name it. Categories
// resolve at read time rather than being stored per row, so a remapping applies to rows already
// written.
func KindsIn(category string) []string {
	var kinds []string
	for kind, in := range categories {
		if in == category {
			kinds = append(kinds, kind)
		}
	}
	sort.Strings(kinds)
	return kinds
}

// Entry is one recorded fact about an account. It carries no user id: that is the query's filter,
// not its result.
type Entry struct {
	ID string

	// Kind is one of the Kind* constants above.
	Kind string

	// Category is CategoryOf(Kind), resolved here so the client filters and the server counts by
	// the same answer.
	Category string

	// SessionID is the session the fact belongs to, empty where there is none: a password change
	// has no session, and clearing every session at once names none of them.
	SessionID string

	// Device is whatever the user agent claimed when the entry was recorded. Untrusted; display
	// only.
	Device string

	// Platform is the operating system family read out of Device, empty when it says nothing
	// recognisable. Derived on the way out rather than stored, so a better parser applies to rows
	// already written.
	Platform string

	IPAddress string

	// OccurredAt is when the fact happened — neither when it was stored nor when it was read.
	OccurredAt time.Time
}

// Query is what a caller asks the feed for. Every zero value means "do not narrow by this".
type Query struct {
	// From and To bound OccurredAt inclusively.
	From time.Time
	To   time.Time

	// Category selects one chip's worth of entries. An unknown category is ignored rather than
	// refused, so a client one release ahead still gets a full feed.
	Category string

	// Search matches a substring of the kind, the device or the address, case-insensitively.
	Search string

	// PageToken is the opaque token a previous Page returned. Empty starts at the newest entry.
	PageToken string

	// PageSize is clamped, never rejected.
	PageSize int
}

// Page is one screenful of the feed, plus what the screen around it needs to describe itself.
type Page struct {
	// Entries, newest first.
	Entries []Entry

	// NextPageToken is empty when this is the last page. A token is a position, not an offset —
	// see the note on Query.PageToken.
	NextPageToken string

	// Total is how many entries match the whole query, so a footer can say "8 of 214". It counts
	// what is being paged through, category filter included.
	Total int

	// Counts is how many entries there are per category, keyed by the Category* constants.
	//
	// Deliberately not narrowed by Query.Category: a chip shows its own count while another chip is
	// active, so counting the filtered set would leave every chip but one at zero. Filled only on
	// the first page — paging does not change them.
	Counts map[string]int

	// KindCounts is how many entries there are per kind, on the same terms as Counts. Sent as well
	// as Counts because a summary card states one exact per-kind fact that a category total cannot
	// yield.
	KindCounts map[string]int

	// RetentionDays is how far back the feed goes, for a footer to report.
	RetentionDays int
}

// Service is the activity module's read surface. There is no write surface.
type Service interface {
	// List returns one page of the account's feed, newest first.
	List(ctx context.Context, userID string, q Query) (Page, error)
}

// Package activityapi is the activity module's public contract.
//
// Other modules import this package and nothing else under internal/modules/activity/. Note what
// is absent: there is no Record on Service. Entries are written by this module reacting to facts the
// other modules announce, and a feed anyone may append to is a feed everyone must call — which is the
// dependency direction the module exists to avoid.
//
// CanReport below is not a hole in that. It governs a path from the *client*, which is not a module
// and cannot be made to announce anything on a bus: nothing on this server can observe which build
// somebody is running or what theme they chose, so those two facts have to arrive from outside or not
// at all. The rule that keeps it safe is different from the rule about modules — the client does not
// get to say what kind of fact it is reporting, it gets to choose from a list of two.
package activityapi

import (
	"context"
	"sort"
	"time"
)

// Feed entry kinds.
//
// These strings cross the wire and the client switches on them to choose a label and an icon, so
// renaming one silently degrades the feed to showing the raw value. Treat them as contract.
//
// They are declared here rather than imported from another module's api because an api package may
// not import another module at all. The duplication is the feature: it lets the account module
// rename its own audit vocabulary without renaming the feed's, and one line in subscriptions.go is
// the entire cost of keeping the two independent.
const (
	KindSignedIn  = "SignedIn"
	KindSignedOut = "SignedOut"

	// KindNewDeviceSignedIn is a sign-in from a device this account has not used before. Still a
	// sign-in, which is why it sits in CategorySignIn — the fact that it is the first one from that
	// device is what the client draws a badge for.
	KindNewDeviceSignedIn = "NewDeviceSignedIn"

	// KindSessionRevoked is a session ended by a decision rather than by leaving. Distinct from
	// KindSignedOut, which until now it was indistinguishable from: both facts arrived on the bus
	// as one event, so the feed said "signed out" whichever had happened.
	KindSessionRevoked = "SessionRevoked"

	// KindSessionRevokedByAdmin is somebody other than the account holder ending their session.
	//
	// Its own kind rather than an attribute of KindSessionRevoked, because the client picks a label
	// and an icon by kind: this is the row that has to look different, and the only one in the feed
	// that says another person acted on your account.
	KindSessionRevokedByAdmin = "SessionRevokedByAdmin"

	// KindFailedSignIn only ever concerns an account that exists — see accountapi.FailedSignIn.
	KindFailedSignIn = "FailedSignIn"

	// KindPasswordChanged has an audit counterpart in the account module; KindSignedOut above does
	// not. That asymmetry is the proof that this vocabulary is its own rather than a re-export.
	KindPasswordChanged = "PasswordChanged"

	// KindAppUpdated and KindThemeChanged are the two facts only the client can know: nothing on
	// this server observes which build somebody is running or what they set their theme to.
	//
	// They are why this module has a write path at all, and they are the entire list of what that
	// path accepts. See ClientReportableKinds.
	KindAppUpdated   = "AppUpdated"
	KindThemeChanged = "ThemeChanged"
)

// clientReportable is the closed set of kinds a client may report about itself.
//
// The hard boundary of the write path, and the reason that path is safe to have. A client that could
// name any kind could write "SignedIn" into somebody's security feed — the exact row a reader trusts
// most — so the server does not take the client's word for what kind of fact this is. It takes a
// choice from a list of two.
//
// Adding to this list is a security decision, not a feature decision. The question to answer first
// is "could a compromised client use this to tell a lie a reader would act on?"
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

// Feed categories.
//
// What a chip along the top of the feed filters by, and what the counts count. Defined here, beside
// the kinds, because the mapping has to live wherever the kinds are named: a client holding its own
// copy is a second place to edit every time a kind is added, and it could not produce the counts
// anyway — those are over everything retained, not over the page that was fetched.
const (
	CategorySignIn   = "SignIn"
	CategorySecurity = "Security"

	// CategorySystem is what happened to the application rather than to the account.
	CategorySystem = "System"
)

// categories is the one table CategoryOf and KindsIn both read.
//
// One table rather than two switch statements, because the two questions are each other's inverse
// and a pair that disagrees is a chip whose count does not match what it shows.
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

// CategoryOf answers which chip an entry belongs under.
//
// An unrecognised kind is Security rather than a category of its own. A kind this build has never
// heard of is either newer than this build or wrong, and both are worth a person's attention more
// than they are worth hiding in a bucket nobody filters by.
func CategoryOf(kind string) string {
	if category, ok := categories[kind]; ok {
		return category
	}
	return CategorySecurity
}

// KindsIn lists the kinds a category selects, sorted, or nil for a category this build does not
// know — which a caller should read as "do not filter" rather than as "match nothing".
//
// Note the asymmetry with CategoryOf, which is deliberate and worth knowing about: a kind written by
// some other build is *counted* under Security, because that is where CategoryOf puts anything it
// does not recognise, but it is not *selected* by the Security chip, because this build cannot name
// it. The alternative is storing the category alongside each row, which would freeze the taxonomy at
// write time and stop a better answer from applying to rows already written. Counting one row in a
// chip that does not list it is the cheaper wrong.
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

// Entry is one thing that happened to an account.
//
// No user id: it is the query's filter rather than its result. The identifier is here, because the
// row belongs to the account reading it — a screen that offers "copy this event" or "report this
// one" needs something to name, and withholding an account's own row id protects nobody.
type Entry struct {
	// ID identifies this entry, for a reader who wants to quote one.
	ID string

	// Kind is one of the Kind* constants above.
	Kind string

	// Category is CategoryOf(Kind), resolved here so the client filters and the server counts by
	// the same answer.
	Category string

	// SessionID is the session the fact belongs to, empty where there is none — a password change
	// has no session, and clearing every session at once names none of them. It is what lets a
	// reader see that nine sign-outs were one session rather than nine.
	SessionID string

	// Device is whatever the user agent claimed when the entry was recorded. Untrusted, and shown
	// only as a hint — the reader is asking "was that me?", not "what browser was that".
	Device string

	// Platform is the operating system family read out of Device, empty when it says nothing
	// recognisable. Derived on the way out rather than stored: it is an opinion about a string, and
	// an opinion that improves should improve for rows already written.
	Platform string

	IPAddress string

	// OccurredAt is when the fact happened, which is neither when it was stored nor when it was
	// read.
	OccurredAt time.Time
}

// Query is what a caller asks the feed for. Every zero value means "do not narrow by this".
type Query struct {
	// From and To bound OccurredAt inclusively.
	From time.Time
	To   time.Time

	// Category selects one chip's worth of entries. A category this build does not know is ignored
	// rather than refused: it would otherwise turn a client that is one release ahead into an error
	// message instead of a full feed.
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
	// Deliberately *not* narrowed by Query.Category: a chip has to show its own count while another
	// chip is the active one, so counting the filtered set would leave every chip but one at zero.
	// Only filled on the first page — paging does not change them, and the client already has them.
	Counts map[string]int

	// KindCounts is how many entries there are per kind, on the same terms as Counts.
	//
	// Sent as well as Counts because the two answer different questions. A chip filters by category;
	// a summary card states one exact fact, and "one sign-in was refused this week" cannot be derived
	// from a Security total that also contains password changes.
	KindCounts map[string]int

	// RetentionDays is how far back the feed goes at all, so a footer can say so rather than leave
	// somebody wondering whether an old entry is missing or expired.
	RetentionDays int
}

// Service is the activity module's read surface. There is no write surface.
type Service interface {
	// List returns one page of the account's feed, newest first.
	List(ctx context.Context, userID string, q Query) (Page, error)
}

// Package activityapi is the activity module's public contract.
//
// Other modules import this package and nothing else under internal/modules/activity/. Note what is
// absent: there is no Record on Service. A feed anyone may append to is a feed everyone must call,
// which is the dependency direction the module exists to avoid.
//
// CanReport below is not a hole in that. It governs a path from the *client*, which is not a module
// and cannot be made to announce anything on a bus: nothing on this server can observe which build
// somebody is running or what theme they chose. The client does not get to say what kind of fact it
// is reporting — it gets to choose from a list of two.
package activityapi

import (
	"context"
	"sort"
	"time"
)

// These strings cross the wire and the client switches on them to choose a label and an icon, so
// renaming one silently degrades the feed to showing the raw value. Treat them as contract.
//
// Declared here rather than imported from another module's api, because an api package may not
// import another module at all. The duplication is the feature: the account module can rename its
// own audit vocabulary without renaming the feed's, and one line in subscriptions.go is the entire
// cost of keeping the two independent.
const (
	KindSignedIn  = "SignedIn"
	KindSignedOut = "SignedOut"

	// Still a sign-in, hence CategorySignIn; the client draws a badge for the "first from this
	// device" part.
	KindNewDeviceSignedIn = "NewDeviceSignedIn"

	// A session ended by a decision rather than by leaving. Distinct from KindSignedOut, which it
	// was indistinguishable from while both facts arrived on the bus as one event — the feed said
	// "signed out" whichever had happened.
	KindSessionRevoked = "SessionRevoked"

	// Its own kind rather than an attribute of KindSessionRevoked, because the client picks a label
	// and an icon by kind: this is the row that has to look different, and the only one in the feed
	// that says another person acted on your account.
	KindSessionRevokedByAdmin = "SessionRevokedByAdmin"

	// Only ever concerns an account that exists — see accountapi.FailedSignIn.
	KindFailedSignIn = "FailedSignIn"

	KindPasswordChanged = "PasswordChanged"

	// The two facts only the client can know: nothing on this server observes which build somebody
	// is running or what they set their theme to. They are why this module has a write path at all,
	// and they are the entire list of what that path accepts.
	KindAppUpdated   = "AppUpdated"
	KindThemeChanged = "ThemeChanged"
)

// The hard boundary of the write path, and the reason that path is safe to have. A client that
// could name any kind could write "SignedIn" into somebody's security feed — the row a reader
// trusts most.
//
// Adding to this list is a security decision, not a feature decision. The question to answer first
// is "could a compromised client use this to tell a lie a reader would act on?"
var clientReportable = map[string]bool{
	KindAppUpdated:   true,
	KindThemeChanged: true,
}

func ClientReportableKinds() []string {
	kinds := make([]string, 0, len(clientReportable))
	for kind := range clientReportable {
		kinds = append(kinds, kind)
	}
	sort.Strings(kinds)
	return kinds
}

func CanReport(kind string) bool {
	return clientReportable[kind]
}

// What a chip along the top of the feed filters by, and what the counts count. Beside the kinds,
// because the mapping has to live wherever the kinds are named: a client holding its own copy is a
// second place to edit every time a kind is added, and it could not produce the counts anyway —
// those are over everything retained, not over the page that was fetched.
const (
	CategorySignIn   = "SignIn"
	CategorySecurity = "Security"

	// What happened to the application rather than to the account.
	CategorySystem = "System"
)

// One table rather than two switch statements, because CategoryOf and KindsIn are each other's
// inverse and a pair that disagrees is a chip whose count does not match what it shows.
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

// An unrecognised kind is Security rather than a category of its own: a kind this build has never
// heard of is either newer or wrong, and both are worth a person's attention more than they are
// worth hiding in a bucket nobody filters by.
func CategoryOf(kind string) string {
	if category, ok := categories[kind]; ok {
		return category
	}
	return CategorySecurity
}

// Nil for a category this build does not know, which a caller should read as "do not filter" rather
// than as "match nothing".
//
// The asymmetry with CategoryOf is deliberate: a kind written by some other build is *counted*
// under Security, because that is where CategoryOf puts anything it does not recognise, but it is
// not *selected* by the Security chip, because this build cannot name it. The alternative is
// storing the category alongside each row, which would freeze the taxonomy at write time and stop a
// better answer from applying to rows already written.
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
// No user id: it is the query's filter rather than its result. The row id is here, because a screen
// that offers "copy this event" needs something to name and withholding an account's own row id
// protects nobody.
type Entry struct {
	ID string

	// One of the Kind* constants above.
	Kind string

	// CategoryOf(Kind), resolved here so the client filters and the server counts by the same
	// answer.
	Category string

	// Empty where there is none — a password change has no session, and clearing every session at
	// once names none of them. It is what lets a reader see that nine sign-outs were one session.
	SessionID string

	// Whatever the user agent claimed. Untrusted, and shown only as a hint.
	Device string

	// The operating system family read out of Device, empty when it says nothing recognisable.
	// Derived on the way out rather than stored, so an opinion that improves improves for rows
	// already written.
	Platform string

	IPAddress string

	// When the fact happened, which is neither when it was stored nor when it was read.
	OccurredAt time.Time
}

// Query is what a caller asks the feed for. Every zero value means "do not narrow by this".
type Query struct {
	// Bound OccurredAt inclusively.
	From time.Time
	To   time.Time

	// A category this build does not know is ignored rather than refused: it would otherwise turn a
	// client one release ahead into an error message instead of a full feed.
	Category string

	// Matches a substring of the kind, the device or the address, case-insensitively.
	Search string

	// The opaque token a previous Page returned. Empty starts at the newest entry.
	PageToken string

	// Clamped, never rejected.
	PageSize int
}

// Page is one screenful of the feed, plus what the screen around it needs to describe itself.
type Page struct {
	// Newest first.
	Entries []Entry

	// Empty when this is the last page. A position, not an offset — see Query.PageToken.
	NextPageToken string

	// How many entries match the whole query, so a footer can say "8 of 214". Category filter
	// included.
	Total int

	// Per category, keyed by the Category* constants.
	//
	// Deliberately *not* narrowed by Query.Category: a chip has to show its own count while another
	// chip is the active one, so counting the filtered set would leave every chip but one at zero.
	// Only filled on the first page — paging does not change them, and the client already has them.
	Counts map[string]int

	// Per kind, on the same terms as Counts. Sent as well as Counts because the two answer
	// different questions: "one sign-in was refused this week" cannot be derived from a Security
	// total that also contains password changes.
	KindCounts map[string]int

	// How far back the feed goes at all, so a footer can say so rather than leave somebody
	// wondering whether an old entry is missing or expired.
	RetentionDays int
}

// The read surface. There is no write surface — see the package doc.
type Service interface {
	// Newest first.
	List(ctx context.Context, userID string, q Query) (Page, error)
}

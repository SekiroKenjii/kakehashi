package service

import (
	"context"
	"errors"
	"testing"
	"time"

	activityapi "__GO_MODULE__/server/internal/modules/activity/api"
	"__GO_MODULE__/server/internal/modules/activity/domain"
	"__GO_MODULE__/server/internal/platform/errs"
)

var occurred = time.Date(2026, time.August, 6, 9, 30, 0, 0, time.UTC)

type fakeStore struct {
	inserted []domain.Entry
	feed     []domain.Entry

	// What List was actually asked for. The clamp is only observable here, because the service
	// returns whatever the store hands back.
	lastTake   int
	lastFilter domain.Filter

	// Every filter the counting queries were given, so a test can assert what they were narrowed by
	// and - more to the point - what they were not.
	countFilters []domain.Filter

	total  int
	byKind map[string]int
	err    error
}

func (f *fakeStore) Insert(_ context.Context, e domain.Entry) error {
	if f.err != nil {
		return f.err
	}
	f.inserted = append(f.inserted, e)
	return nil
}

func (f *fakeStore) List(
	_ context.Context, _ string, filter domain.Filter, take int,
) ([]domain.Entry, error) {
	f.lastTake = take
	f.lastFilter = filter
	if f.err != nil {
		return nil, f.err
	}
	return f.feed, nil
}

func (f *fakeStore) Count(_ context.Context, _ string, filter domain.Filter) (int, error) {
	f.countFilters = append(f.countFilters, filter)
	if f.err != nil {
		return 0, f.err
	}
	return f.total, nil
}

func (f *fakeStore) CountByKind(
	_ context.Context, _ string, filter domain.Filter,
) (map[string]int, error) {
	f.countFilters = append(f.countFilters, filter)
	if f.err != nil {
		return nil, f.err
	}
	return f.byKind, nil
}

func newService(store *fakeStore) *Service {
	sequence := 0
	return New(store, func() string {
		sequence++
		return "id-" + string(rune('0'+sequence))
	})
}

func TestRecordStoresTheFactItWasGiven(t *testing.T) {
	store := &fakeStore{}

	err := newService(store).Record(
		context.Background(), "account-1", "SignedIn", "session-1", "laptop", "10.0.0.1", occurred)

	if err != nil {
		t.Fatalf("Record returned an error: %v", err)
	}
	if len(store.inserted) != 1 {
		t.Fatalf("store holds %d entries, want 1", len(store.inserted))
	}

	got := store.inserted[0]
	if got.UserID != "account-1" || got.Kind != "SignedIn" || got.SessionID != "session-1" ||
		got.Device != "laptop" || got.IPAddress != "10.0.0.1" || !got.OccurredAt.Equal(occurred) {
		t.Errorf("stored %+v, want the fact as passed", got)
	}
	if got.ID == "" {
		t.Error("stored entry has no id")
	}
}

func TestRecordWithoutAnAccountNeverReachesTheStore(t *testing.T) {
	store := &fakeStore{}

	err := newService(store).Record(
		context.Background(), "", "SignedIn", "session-1", "laptop", "10.0.0.1", occurred)

	if errs.KindOf(err) != errs.Invalid {
		t.Errorf("kind = %v, want %v", errs.KindOf(err), errs.Invalid)
	}
	if len(store.inserted) != 0 {
		t.Errorf("store holds %d entries, want none", len(store.inserted))
	}
}

func TestListClampsThePageSize(t *testing.T) {
	cases := []struct {
		name string
		size int
		want int
	}{
		{"zero means the default", 0, 50},
		{"negative means the default", -3, 50},
		{"absurd is clamped down to the ceiling", 10_000, 200},
		{"reasonable is honoured", 5, 5},
	}

	for _, c := range cases {
		t.Run(c.name, func(t *testing.T) {
			store := &fakeStore{}
			_, err := newService(store).List(
				context.Background(), "account-1", activityapi.Query{PageSize: c.size})
			if err != nil {
				t.Fatalf("List returned an error: %v", err)
			}
			// One past the page, which is how the service knows whether there is a next one.
			if store.lastTake != c.want+1 {
				t.Errorf("store asked for %d rows, want %d (the page plus one probe row)",
					store.lastTake, c.want+1)
			}
		})
	}
}

func TestListReturnsWhatTheFeedDrawsAndKeepsTheAccountIdBack(t *testing.T) {
	store := &fakeStore{feed: []domain.Entry{{
		ID:         "id-1",
		UserID:     "account-1",
		Kind:       "SignedIn",
		SessionID:  "session-1",
		Device:     "__APP_NAME__/1.1.2 (Windows NT 10.0; Win64)",
		IPAddress:  "10.0.0.1",
		OccurredAt: occurred,
	}}}

	page, err := newService(store).List(
		context.Background(), "account-1", activityapi.Query{PageSize: 10})
	if err != nil {
		t.Fatalf("List returned an error: %v", err)
	}

	if len(page.Entries) != 1 {
		t.Fatalf("got %d entries, want 1", len(page.Entries))
	}

	// The row id crosses; the account id does not, and activityapi.Entry has no field for it. That
	// half is a compile-time fact, so this asserts the rest of the mapping stays complete.
	got := page.Entries[0]
	if got.ID != "id-1" || got.Kind != "SignedIn" || got.SessionID != "session-1" ||
		got.IPAddress != "10.0.0.1" || !got.OccurredAt.Equal(occurred) {
		t.Errorf("entry = %+v, want the stored fact", got)
	}

	// Category and Platform are derived on the way out rather than stored, so a mapping that forgot
	// to derive them would still return a plausible-looking row. Assert they arrived.
	if got.Category != activityapi.CategorySignIn {
		t.Errorf("category = %q, want %q", got.Category, activityapi.CategorySignIn)
	}
	if got.Platform != "Windows" {
		t.Errorf("platform = %q, want Windows", got.Platform)
	}
}

func TestStoreFailuresPropagateUnchanged(t *testing.T) {
	broken := errors.New("mongo is down")
	store := &fakeStore{err: broken}
	svc := newService(store)

	recordErr := svc.Record(
		context.Background(), "account-1", "SignedIn", "session-1", "laptop", "10.0.0.1", occurred)
	_, listErr := svc.List(context.Background(), "account-1", activityapi.Query{PageSize: 10})

	if !errors.Is(recordErr, broken) {
		t.Errorf("Record returned %v, want the store's error", recordErr)
	}
	if !errors.Is(listErr, broken) {
		t.Errorf("List returned %v, want the store's error", listErr)
	}
}

func entriesAt(count int) []domain.Entry {
	out := make([]domain.Entry, count)
	for i := range out {
		out[i] = domain.Entry{
			ID:         "id-" + string(rune('a'+i)),
			UserID:     "account-1",
			Kind:       "SignedIn",
			OccurredAt: occurred.Add(time.Duration(-i) * time.Minute),
		}
	}
	return out
}

// A page that comes back exactly full is indistinguishable from the last page, which is why the read
// asks for one row past it. The probe row must not be shown, and the token must point at the last row
// that was.
func TestAFullPageOffersTheNextOneWithoutShowingTheProbeRow(t *testing.T) {
	store := &fakeStore{feed: entriesAt(3)}

	page, err := newService(store).List(
		context.Background(), "account-1", activityapi.Query{PageSize: 2})
	if err != nil {
		t.Fatalf("List returned an error: %v", err)
	}

	if len(page.Entries) != 2 {
		t.Fatalf("drew %d entries, want 2 - the probe row leaked onto the page", len(page.Entries))
	}
	if page.NextPageToken == "" {
		t.Fatal("no next page offered, but there was another row")
	}

	cursor, err := decodeCursor(page.NextPageToken)
	if err != nil {
		t.Fatalf("the service issued a token it cannot read back: %v", err)
	}
	last := page.Entries[1]
	if cursor.ID != last.ID || !cursor.OccurredAt.Equal(last.OccurredAt) {
		t.Errorf("cursor = %+v, want the last row drawn (%s at %v)", cursor, last.ID, last.OccurredAt)
	}
}

func TestAShortPageIsTheLastOne(t *testing.T) {
	store := &fakeStore{feed: entriesAt(2)}

	page, err := newService(store).List(
		context.Background(), "account-1", activityapi.Query{PageSize: 5})
	if err != nil {
		t.Fatalf("List returned an error: %v", err)
	}

	if page.NextPageToken != "" {
		t.Errorf("offered a next page (%q) with nothing on it", page.NextPageToken)
	}
}

// Round-tripped rather than inspected: the token's shape is deliberately the server's business, so
// what a test can honestly assert is that a token the service issued gets the caller back to the
// position it named.
func TestATokenTheServiceIssuedReachesTheStoreAsAPosition(t *testing.T) {
	first := &fakeStore{feed: entriesAt(3)}
	svc := newService(first)

	page, err := svc.List(context.Background(), "account-1", activityapi.Query{PageSize: 2})
	if err != nil {
		t.Fatalf("List returned an error: %v", err)
	}

	second := &fakeStore{feed: entriesAt(1)}
	if _, err := newService(second).List(context.Background(), "account-1", activityapi.Query{
		PageSize:  2,
		PageToken: page.NextPageToken,
	}); err != nil {
		t.Fatalf("List with a token returned an error: %v", err)
	}

	if second.lastFilter.After == nil {
		t.Fatal("the second read was not narrowed by a cursor")
	}
	want := page.Entries[1]
	if second.lastFilter.After.ID != want.ID {
		t.Errorf("cursor id = %q, want %q", second.lastFilter.After.ID, want.ID)
	}
}

// Refused rather than ignored. Starting over from the newest entry would draw page one again beneath
// a "load more" button, and a reader would conclude the feed loops.
func TestATokenThisServerDidNotWriteIsRefused(t *testing.T) {
	cases := []struct {
		name  string
		token string
	}{
		{"not base64", "!!!not-a-token!!!"},
		{"base64 but not a cursor", "aGVsbG8"},
		{"no id", "MjAyNi0wOC0wNlQwOTozMDowMFp8"},
		{"unparseable time", "bm90LWEtdGltZXxpZC0x"},
	}

	for _, c := range cases {
		t.Run(c.name, func(t *testing.T) {
			store := &fakeStore{}
			_, err := newService(store).List(context.Background(), "account-1", activityapi.Query{
				PageToken: c.token,
			})

			if errs.KindOf(err) != errs.Invalid {
				t.Errorf("kind = %v, want %v", errs.KindOf(err), errs.Invalid)
			}
			if store.lastTake != 0 {
				t.Error("the store was read despite an unreadable token")
			}
		})
	}
}

// The counts are what every chip shows while one of them is active, so they must be taken over the
// whole set rather than over the filtered view. The range and the search still apply - they are what
// the reader chose to look at; the category is not.
func TestTheChipCountsIgnoreTheChipThatIsActive(t *testing.T) {
	store := &fakeStore{byKind: map[string]int{
		"SignedIn":        9,
		"SignedOut":       5,
		"FailedSignIn":    1,
		"PasswordChanged": 2,
	}}
	from := occurred.Add(-7 * 24 * time.Hour)

	page, err := newService(store).List(context.Background(), "account-1", activityapi.Query{
		Category: activityapi.CategorySecurity,
		Search:   "laptop",
		From:     from,
		PageSize: 10,
	})
	if err != nil {
		t.Fatalf("List returned an error: %v", err)
	}

	if page.Counts[activityapi.CategorySignIn] != 14 {
		t.Errorf("SignIn count = %d, want 14", page.Counts[activityapi.CategorySignIn])
	}
	if page.Counts[activityapi.CategorySecurity] != 3 {
		t.Errorf("Security count = %d, want 3", page.Counts[activityapi.CategorySecurity])
	}

	// The per-kind numbers come from the same aggregation rather than a second one: a card that says
	// "one sign-in was refused" cannot read that off a Security total containing password changes.
	if page.KindCounts["FailedSignIn"] != 1 {
		t.Errorf("FailedSignIn count = %d, want 1", page.KindCounts["FailedSignIn"])
	}
	if len(store.countFilters) != 2 {
		t.Errorf("ran %d counting queries, want 2 (one total, one grouped)", len(store.countFilters))
	}

	// The counting query kept the reader's range and text, and dropped only the chip.
	var counted domain.Filter
	for _, f := range store.countFilters {
		if f.Kinds == nil {
			counted = f
		}
	}
	if !counted.From.Equal(from) || counted.Query != "laptop" {
		t.Errorf("counted with %+v, want the reader's range and search kept", counted)
	}

	// And the page itself was narrowed by the chip.
	if len(store.lastFilter.Kinds) == 0 {
		t.Error("the page was not narrowed by the active chip")
	}
}

// Paging cannot change the counts and the client already has them, so a later page does not pay for
// an aggregation to send them again.
func TestALaterPageDoesNotRecountTheChips(t *testing.T) {
	store := &fakeStore{feed: entriesAt(1), byKind: map[string]int{"SignedIn": 1}}

	page, err := newService(store).List(context.Background(), "account-1", activityapi.Query{
		PageSize:  2,
		PageToken: encodeCursor(domain.Cursor{OccurredAt: occurred, ID: "id-a"}),
	})
	if err != nil {
		t.Fatalf("List returned an error: %v", err)
	}

	if page.Counts != nil {
		t.Errorf("counts = %v, want none on a later page", page.Counts)
	}
}

func TestThePageReportsTheTotalAndHowFarBackTheFeedGoes(t *testing.T) {
	store := &fakeStore{feed: entriesAt(1), total: 214}

	page, err := newService(store).List(
		context.Background(), "account-1", activityapi.Query{PageSize: 10})
	if err != nil {
		t.Fatalf("List returned an error: %v", err)
	}

	if page.Total != 214 {
		t.Errorf("Total = %d, want 214", page.Total)
	}
	if page.RetentionDays != 90 {
		t.Errorf("RetentionDays = %d, want 90", page.RetentionDays)
	}
}

// An unknown category means "do not narrow" rather than "match nothing": a client one release ahead
// should see the whole feed, not an empty one.
func TestAnUnknownCategoryDoesNotEmptyTheFeed(t *testing.T) {
	store := &fakeStore{feed: entriesAt(2)}

	page, err := newService(store).List(context.Background(), "account-1", activityapi.Query{
		Category: "SomethingALaterBuildAdded",
		PageSize: 10,
	})
	if err != nil {
		t.Fatalf("List returned an error: %v", err)
	}

	if len(store.lastFilter.Kinds) != 0 {
		t.Errorf("narrowed by %v, want no narrowing at all", store.lastFilter.Kinds)
	}
	if len(page.Entries) != 2 {
		t.Errorf("drew %d entries, want 2", len(page.Entries))
	}
}

// "8 of 214" means 214 match, not 214 are left below where you are. Now that the store counts
// exactly what it is handed, dropping the cursor is a decision this layer makes and a test can see.
func TestTheTotalCountsWhatMatchesRatherThanWhatIsLeft(t *testing.T) {
	store := &fakeStore{feed: entriesAt(1), total: 214}

	page, err := newService(store).List(context.Background(), "account-1", activityapi.Query{
		PageSize:  2,
		PageToken: encodeCursor(domain.Cursor{OccurredAt: occurred, ID: "id-a"}),
	})
	if err != nil {
		t.Fatalf("List returned an error: %v", err)
	}

	if page.Total != 214 {
		t.Errorf("Total = %d, want 214", page.Total)
	}
	if len(store.countFilters) == 0 {
		t.Fatal("nothing was counted")
	}
	for _, f := range store.countFilters {
		if f.After != nil {
			t.Error("a counting query was narrowed by the cursor, so the total would shrink as you page")
		}
	}
	// The page itself still is, or paging would draw the same rows forever.
	if store.lastFilter.After == nil {
		t.Error("the page was not narrowed by the cursor")
	}
}

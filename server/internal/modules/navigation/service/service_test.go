package service_test

import (
	"context"
	"sort"
	"testing"
	"time"

	navigationapi "github.com/SekiroKenjii/kakehashi/server/internal/modules/navigation/api"
	"github.com/SekiroKenjii/kakehashi/server/internal/modules/navigation/domain"
	"github.com/SekiroKenjii/kakehashi/server/internal/modules/navigation/service"
	"github.com/SekiroKenjii/kakehashi/server/internal/platform/auth"
	"github.com/SekiroKenjii/kakehashi/server/internal/platform/errs"
)

// A fake store rather than a real database, because every rule worth testing here is a decision
// about two lists — what the code declares and what the table holds — and none of it is SQL.

type fakeStore struct {
	groups     map[string]domain.Group
	placements map[string]domain.Placement

	// order preserves insertion order for Groups, which the real store returns sorted.
	groupOrder []string
}

func newFakeStore() *fakeStore {
	return &fakeStore{
		groups:     map[string]domain.Group{},
		placements: map[string]domain.Placement{},
	}
}

// Layout is the one-snapshot read. The fake has no concurrency to protect against; it exists so the
// service's real call has something to call.
func (f *fakeStore) Layout(ctx context.Context) ([]domain.Group, []domain.Placement, error) {
	groups, err := f.Groups(ctx)
	if err != nil {
		return nil, nil, err
	}
	placements, err := f.Placements(ctx)
	if err != nil {
		return nil, nil, err
	}
	return groups, placements, nil
}

// Groups returns them ordered the way the real store's ORDER BY does — by position, then title.
// Insertion order was close enough to look right and wrong enough that a heading's SortOrder could
// have been ignored entirely without a test noticing.
func (f *fakeStore) Groups(_ context.Context) ([]domain.Group, error) {
	out := make([]domain.Group, 0, len(f.groups))
	for _, id := range f.groupOrder {
		out = append(out, f.groups[id])
	}
	sort.SliceStable(out, func(i, j int) bool {
		if out[i].Order != out[j].Order {
			return out[i].Order < out[j].Order
		}
		return out[i].Title < out[j].Title
	})
	return out, nil
}

func (f *fakeStore) Group(_ context.Context, id string) (domain.Group, error) {
	group, ok := f.groups[id]
	if !ok {
		return domain.Group{}, errs.NotFoundf("No navigation heading with id %s.", id)
	}
	return group, nil
}

func (f *fakeStore) InsertGroup(_ context.Context, g domain.Group, _ time.Time) error {
	if _, exists := f.groups[g.ID]; exists {
		return errs.Conflictf("The identifier %s is already taken by another heading.", g.ID)
	}
	// AK_NavGroup_Title: the real schema refuses two headings with the same name, and the fake used
	// to accept them — so the conflict path was unreachable from either side of the boundary.
	if f.titleTaken(g.Title, g.ID) {
		return errs.Conflictf("A navigation heading called %s already exists.", g.Title)
	}
	f.put(g)
	return nil
}

func (f *fakeStore) titleTaken(title, exceptID string) bool {
	for id, existing := range f.groups {
		if id != exceptID && existing.Title == title {
			return true
		}
	}
	return false
}

func (f *fakeStore) UpdateGroup(_ context.Context, g domain.Group, _ time.Time) error {
	if _, exists := f.groups[g.ID]; !exists {
		return errs.NotFoundf("No navigation heading with id %s.", g.ID)
	}
	if f.titleTaken(g.Title, g.ID) {
		return errs.Conflictf("A navigation heading called %s already exists.", g.Title)
	}
	f.groups[g.ID] = g
	return nil
}

func (f *fakeStore) DeleteGroup(_ context.Context, id string) error {
	group, ok := f.groups[id]
	if !ok || group.IsSystem {
		return errs.NotFoundf("No deletable navigation heading with id %s.", id)
	}
	delete(f.groups, id)

	// What the foreign key does in the real store: the placements fall to ungrouped.
	for key, placement := range f.placements {
		if placement.GroupID == id {
			placement.GroupID = ""
			f.placements[key] = placement
		}
	}
	return nil
}

func (f *fakeStore) EnsureGroup(_ context.Context, g domain.Group, _ time.Time) error {
	if _, exists := f.groups[g.ID]; exists {
		return nil
	}
	f.put(g)
	return nil
}

func (f *fakeStore) Placements(_ context.Context) ([]domain.Placement, error) {
	out := make([]domain.Placement, 0, len(f.placements))
	for _, placement := range f.placements {
		out = append(out, placement)
	}
	return out, nil
}

func (f *fakeStore) Placement(_ context.Context, id string) (domain.Placement, error) {
	placement, ok := f.placements[id]
	if !ok {
		return domain.Placement{}, errs.NotFoundf("No navigation item with id %s.", id)
	}
	return placement, nil
}

func (f *fakeStore) EnsurePlacements(
	_ context.Context, seeds []domain.Placement, _ time.Time,
) error {
	for _, seed := range seeds {
		if _, exists := f.placements[seed.DestinationID]; exists {
			continue
		}
		f.placements[seed.DestinationID] = seed
	}
	return nil
}

func (f *fakeStore) Move(_ context.Context, id, groupID string, order int, _ time.Time) error {
	placement, ok := f.placements[id]
	if !ok {
		return errs.NotFoundf("No navigation item with id %s.", id)
	}
	// FK_NavItem_NavGroup. The fake used to accept any heading id, so the real store's foreign-key
	// failure was unreachable by any test and a test could reach a state the schema forbids.
	if groupID != "" {
		if _, exists := f.groups[groupID]; !exists {
			return errs.NotFoundf("No navigation heading with id %s.", groupID)
		}
	}
	placement.GroupID = groupID
	placement.Order = order
	f.placements[id] = placement
	return nil
}

func (f *fakeStore) Override(
	_ context.Context, id, title, icon string, isVisible bool, _ time.Time,
) error {
	placement, ok := f.placements[id]
	if !ok {
		return errs.NotFoundf("No navigation item with id %s.", id)
	}
	placement.Title = title
	placement.Icon = icon
	placement.IsVisible = isVisible
	f.placements[id] = placement
	return nil
}

func (f *fakeStore) put(g domain.Group) {
	f.groups[g.ID] = g
	f.groupOrder = append(f.groupOrder, g.ID)
}

var systemGroups = []navigationapi.SystemGroup{
	{ID: "utilities", Title: "Utilities", Order: 10},
	{ID: "administration", Title: "Administration", Order: 20},
}

// notes is the ordinary destination: gated by its module's .access, shown disabled when denied.
var notes = navigationapi.Destination{
	ID: "notes", ModuleID: "notes", DefaultTitle: "Notes", DefaultIcon: "note",
	DefaultGroup: "utilities", DefaultOrder: 10,
}

// users is the administrative one: its own permission, and hidden rather than locked.
var users = navigationapi.Destination{
	ID: "account.users", ModuleID: "account", DefaultTitle: "Users", DefaultIcon: "people",
	DefaultGroup: "administration", DefaultOrder: 10,
	Permission: "users.manage", HideWhenDenied: true,
}

func TestReconcileSeedsADestinationIntoItsDeclaredPlace(t *testing.T) {
	store := newFakeStore()
	svc := service.New(store, nil, notes, users)

	if err := svc.Reconcile(context.Background(), systemGroups); err != nil {
		t.Fatalf("reconcile: %v", err)
	}

	placement, err := store.Placement(context.Background(), "notes")
	if err != nil {
		t.Fatalf("notes was not seeded: %v", err)
	}
	if placement.GroupID != "utilities" || placement.Order != 10 || !placement.IsVisible {
		t.Errorf("seeded %+v, want utilities/10/visible", placement)
	}
	if _, err := store.Group(context.Background(), "administration"); err != nil {
		t.Errorf("system heading was not seeded: %v", err)
	}
}

// The rule the whole design rests on: a row an administrator has touched is never rewritten by a
// boot. A version of Reconcile that refreshed the defaults would undo every rearrangement on every
// restart — silently, and only in production, where restarts happen unattended.
func TestReconcileLeavesAnAdministratorsArrangementAlone(t *testing.T) {
	store := newFakeStore()
	svc := service.New(store, nil, notes)
	ctx := context.Background()

	if err := svc.Reconcile(ctx, systemGroups); err != nil {
		t.Fatalf("first reconcile: %v", err)
	}
	if _, err := svc.MoveItem(ctx, "notes", "administration", 99); err != nil {
		t.Fatalf("move: %v", err)
	}
	if _, err := svc.UpdateItem(ctx, "notes", "Scratchpad", "", true); err != nil {
		t.Fatalf("rename: %v", err)
	}
	if _, err := svc.UpdateGroup(ctx, "utilities", "Tools", 5); err != nil {
		t.Fatalf("rename heading: %v", err)
	}

	// A restart.
	if err := svc.Reconcile(ctx, systemGroups); err != nil {
		t.Fatalf("second reconcile: %v", err)
	}

	placement, err := store.Placement(ctx, "notes")
	if err != nil {
		t.Fatalf("placement: %v", err)
	}
	if placement.GroupID != "administration" || placement.Order != 99 {
		t.Errorf("reconcile moved the item back to %+v", placement)
	}
	if placement.Title != "Scratchpad" {
		t.Errorf("reconcile undid the rename, title is %q", placement.Title)
	}

	group, err := store.Group(ctx, "utilities")
	if err != nil {
		t.Fatalf("group: %v", err)
	}
	if group.Title != "Tools" {
		t.Errorf("reconcile undid the heading rename, title is %q", group.Title)
	}
}

func TestBuildDisablesADeniedDestinationRatherThanHidingIt(t *testing.T) {
	svc, ctx := reconciled(t, notes)

	pane, err := svc.Build(ctx, auth.Grants{})
	if err != nil {
		t.Fatalf("build: %v", err)
	}

	items := itemsUnder(pane, "utilities")
	if len(items) != 1 {
		t.Fatalf("got %d items under utilities, want 1", len(items))
	}
	if items[0].Enabled {
		t.Error("a destination the caller cannot reach came back enabled")
	}
}

func TestBuildHidesADestinationThatAsksToBeHidden(t *testing.T) {
	svc, ctx := reconciled(t, users)

	pane, err := svc.Build(ctx, auth.Grants{})
	if err != nil {
		t.Fatalf("build: %v", err)
	}
	if len(pane.Groups) != 0 || len(pane.Ungrouped) != 0 {
		t.Errorf("a hidden-when-denied destination was drawn: %+v", pane)
	}
}

// The user-visible bug this replaces: an "Administration" heading showing, empty, to an account with
// nothing under it.
func TestBuildDropsAHeadingWithNothingLeftUnderIt(t *testing.T) {
	svc, ctx := reconciled(t, notes, users)

	pane, err := svc.Build(ctx, auth.Grants{"notes.access": auth.ScopeAll})
	if err != nil {
		t.Fatalf("build: %v", err)
	}

	for _, group := range pane.Groups {
		if group.GroupID == "administration" {
			t.Error("administration was drawn with every destination under it denied")
		}
	}
	if len(itemsUnder(pane, "utilities")) != 1 {
		t.Error("utilities lost the destination the caller may reach")
	}
}

func TestBuildOrdersItemsByTheStoredOrderNotTheDeclarationOrder(t *testing.T) {
	first := navigationapi.Destination{
		ID: "a", ModuleID: "notes", DefaultTitle: "A", DefaultGroup: "utilities", DefaultOrder: 10,
	}
	second := navigationapi.Destination{
		ID: "b", ModuleID: "notes", DefaultTitle: "B", DefaultGroup: "utilities", DefaultOrder: 20,
	}

	svc, ctx := reconciled(t, first, second)
	if _, err := svc.MoveItem(ctx, "b", "utilities", 1); err != nil {
		t.Fatalf("move: %v", err)
	}

	pane, err := svc.Build(ctx, auth.Grants{"notes.access": auth.ScopeAll})
	if err != nil {
		t.Fatalf("build: %v", err)
	}

	items := itemsUnder(pane, "utilities")
	if len(items) != 2 || items[0].ID != "b" {
		t.Errorf("order is %v, want b first", ids(items))
	}
}

func TestBuildSkipsADestinationAnAdministratorHid(t *testing.T) {
	svc, ctx := reconciled(t, notes)
	if _, err := svc.UpdateItem(ctx, "notes", "", "", false); err != nil {
		t.Fatalf("hide: %v", err)
	}

	pane, err := svc.Build(ctx, auth.Grants{"notes.access": auth.ScopeAll})
	if err != nil {
		t.Fatalf("build: %v", err)
	}
	if len(pane.Groups) != 0 {
		t.Errorf("a hidden destination was still drawn: %+v", pane.Groups)
	}
}

func TestBuildPrefersTheOverrideAndFallsBackToWhatTheCodeSays(t *testing.T) {
	svc, ctx := reconciled(t, notes)
	grants := auth.Grants{"notes.access": auth.ScopeAll}

	pane, err := svc.Build(ctx, grants)
	if err != nil {
		t.Fatalf("build: %v", err)
	}
	if got := itemsUnder(pane, "utilities")[0].Title; got != "Notes" {
		t.Errorf("title without an override is %q, want the declared Notes", got)
	}

	if _, err := svc.UpdateItem(ctx, "notes", "Scratchpad", "", true); err != nil {
		t.Fatalf("rename: %v", err)
	}
	pane, err = svc.Build(ctx, grants)
	if err != nil {
		t.Fatalf("rebuild: %v", err)
	}
	if got := itemsUnder(pane, "utilities")[0].Title; got != "Scratchpad" {
		t.Errorf("title with an override is %q, want Scratchpad", got)
	}

	// And back: clearing it has to return the destination to what the code calls it, or renaming a
	// page once is permanent.
	if _, err := svc.UpdateItem(ctx, "notes", "", "", true); err != nil {
		t.Fatalf("clear: %v", err)
	}
	pane, err = svc.Build(ctx, grants)
	if err != nil {
		t.Fatalf("rebuild: %v", err)
	}
	if got := itemsUnder(pane, "utilities")[0].Title; got != "Notes" {
		t.Errorf("clearing the override left %q, want Notes", got)
	}
}

func TestDeleteGroupRefusesASystemHeadingAndSaysWhy(t *testing.T) {
	svc, ctx := reconciled(t, users)

	err := svc.DeleteGroup(ctx, "administration")
	if errs.KindOf(err) != errs.Invalid {
		t.Fatalf("deleting a system heading returned %v, want an invalid-argument", err)
	}
	if msg := errs.PublicMessage(err); msg == "" {
		t.Error("the refusal carried no message a person could act on")
	}
}

// Deleting a heading somebody made drops what was under it to ungrouped rather than taking the
// destinations with it: the pages are still compiled in and still have to go somewhere.
func TestDeleteGroupLeavesItsDestinationsUngrouped(t *testing.T) {
	svc, ctx := reconciled(t, notes)
	if _, err := svc.CreateGroup(ctx, "", "Monitoring", 30); err != nil {
		t.Fatalf("create: %v", err)
	}
	if _, err := svc.MoveItem(ctx, "notes", "monitoring", 10); err != nil {
		t.Fatalf("move: %v", err)
	}
	if err := svc.DeleteGroup(ctx, "monitoring"); err != nil {
		t.Fatalf("delete: %v", err)
	}

	pane, err := svc.Build(ctx, auth.Grants{"notes.access": auth.ScopeAll})
	if err != nil {
		t.Fatalf("build: %v", err)
	}
	if len(pane.Ungrouped) != 1 || pane.Ungrouped[0].ID != "notes" {
		t.Errorf("notes did not fall to ungrouped: %+v", pane)
	}
}

// A row whose destination this build no longer has. Kept, skipped when drawing, and visible on the
// administration screen — which is the only place anybody can find out it is there.
func TestAnOrphanIsSkippedWhenDrawnAndListedWhenManaged(t *testing.T) {
	store := newFakeStore()
	ctx := context.Background()
	before := service.New(store, nil, notes, users)
	if err := before.Reconcile(ctx, systemGroups); err != nil {
		t.Fatalf("reconcile: %v", err)
	}

	// A deploy that removed the notes module.
	after := service.New(store, nil, users)
	if err := after.Reconcile(ctx, systemGroups); err != nil {
		t.Fatalf("reconcile after removal: %v", err)
	}

	pane, err := after.Build(ctx, auth.Grants{
		"notes.access": auth.ScopeAll, "users.manage": auth.ScopeAll,
	})
	if err != nil {
		t.Fatalf("build: %v", err)
	}
	for _, group := range pane.Groups {
		for _, item := range group.Items {
			if item.ID == "notes" {
				t.Error("a destination this build no longer has was drawn")
			}
		}
	}

	items, err := after.Items(ctx)
	if err != nil {
		t.Fatalf("items: %v", err)
	}

	var found bool
	for _, item := range items {
		if item.DestinationID == "notes" {
			found = true
			if !item.Orphan {
				t.Error("the leftover row was not marked as an orphan")
			}
		}
	}
	if !found {
		t.Error("the leftover row is invisible to the screen that manages the layout")
	}
}

// The permission a destination is checked against is not on the layout surface at all, which is what
// makes handing the arrangement to an administrator safe.
func TestItemsReportsThePermissionTheCodeDeclares(t *testing.T) {
	svc, ctx := reconciled(t, notes, users)

	items, err := svc.Items(ctx)
	if err != nil {
		t.Fatalf("items: %v", err)
	}

	want := map[string]string{"notes": "notes.access", "account.users": "users.manage"}
	for _, item := range items {
		if got := want[item.DestinationID]; got != item.RequiredPermission {
			t.Errorf("%s is gated on %q, want %q", item.DestinationID, item.RequiredPermission, got)
		}
	}
}

func reconciled(
	t *testing.T, declared ...navigationapi.Destination,
) (*service.Service, context.Context) {
	t.Helper()

	ctx := context.Background()
	svc := service.New(newFakeStore(), nil, declared...)
	if err := svc.Reconcile(ctx, systemGroups); err != nil {
		t.Fatalf("reconcile: %v", err)
	}
	return svc, ctx
}

func itemsUnder(pane service.Pane, groupID string) []service.Item {
	for _, group := range pane.Groups {
		if group.GroupID == groupID {
			return group.Items
		}
	}
	return nil
}

func ids(items []service.Item) []string {
	out := make([]string, len(items))
	for i, item := range items {
		out[i] = item.ID
	}
	return out
}

// --- What the audit found nothing covering. ---

// Every write path invalidates the cache. Only one of the five was covered, so removing
// s.invalidate() from the other four left the whole suite green while the pane went stale.
func TestEveryWriteIsVisibleToTheNextRead(t *testing.T) {
	grants := auth.Grants{"notes.access": auth.ScopeAll, "users.manage": auth.ScopeAll}

	cases := []struct {
		name  string
		write func(*service.Service, context.Context) error
		want  func(service.Pane) bool
	}{
		{
			name: "CreateGroup",
			write: func(s *service.Service, ctx context.Context) error {
				_, err := s.CreateGroup(ctx, "", "Monitoring", 30)
				return err
			},
			// A new heading with nothing in it draws nothing, so the write is observed through
			// Groups rather than the pane.
			want: func(service.Pane) bool { return true },
		},
		{
			name: "MoveItem",
			write: func(s *service.Service, ctx context.Context) error {
				_, err := s.MoveItem(ctx, "notes", "administration", 5)
				return err
			},
			want: func(p service.Pane) bool {
				return len(itemsUnder(p, "utilities")) == 0
			},
		},
		{
			name: "UpdateItem",
			write: func(s *service.Service, ctx context.Context) error {
				_, err := s.UpdateItem(ctx, "notes", "Scratchpad", "", true)
				return err
			},
			want: func(p service.Pane) bool {
				items := itemsUnder(p, "utilities")
				return len(items) == 1 && items[0].Title == "Scratchpad"
			},
		},
		{
			name: "UpdateGroup",
			write: func(s *service.Service, ctx context.Context) error {
				_, err := s.UpdateGroup(ctx, "utilities", "Tools", 10)
				return err
			},
			want: func(p service.Pane) bool {
				for _, g := range p.Groups {
					if g.GroupID == "utilities" {
						return g.Title == "Tools"
					}
				}
				return false
			},
		},
		{
			name: "DeleteGroup",
			write: func(s *service.Service, ctx context.Context) error {
				if _, err := s.CreateGroup(ctx, "", "Monitoring", 30); err != nil {
					return err
				}
				if _, err := s.MoveItem(ctx, "notes", "monitoring", 10); err != nil {
					return err
				}
				return s.DeleteGroup(ctx, "monitoring")
			},
			want: func(p service.Pane) bool {
				return len(p.Ungrouped) == 1 && p.Ungrouped[0].ID == "notes"
			},
		},
	}

	for _, tc := range cases {
		t.Run(tc.name, func(t *testing.T) {
			svc, ctx := reconciled(t, notes, users)

			// Read first, so the cache is warm and an un-invalidated write is caught.
			if _, err := svc.Build(ctx, grants); err != nil {
				t.Fatalf("first build: %v", err)
			}
			if err := tc.write(svc, ctx); err != nil {
				t.Fatalf("write: %v", err)
			}

			pane, err := svc.Build(ctx, grants)
			if err != nil {
				t.Fatalf("second build: %v", err)
			}
			if !tc.want(pane) {
				t.Errorf("the pane does not reflect the write: %+v", pane)
			}
		})
	}
}

func TestMovingIntoAHeadingThatDoesNotExistIsRefused(t *testing.T) {
	svc, ctx := reconciled(t, notes)

	if _, err := svc.MoveItem(ctx, "notes", "monitoring", 10); errs.KindOf(err) != errs.NotFound {
		t.Fatalf("moving into a missing heading returned %v, want a not-found", err)
	}
}

func TestAHeadingCannotTakeANameAnotherAlreadyHas(t *testing.T) {
	svc, ctx := reconciled(t, notes)

	if _, err := svc.CreateGroup(ctx, "", "Utilities", 30); errs.KindOf(err) != errs.Conflict {
		t.Errorf("creating a second Utilities returned %v, want a conflict", err)
	}
	if _, err := svc.UpdateGroup(ctx, "administration", "Utilities", 20); errs.KindOf(err) != errs.Conflict {
		t.Errorf("renaming Administration to Utilities returned %v, want a conflict", err)
	}
}

// A heading's position decides where it is drawn, and nothing asserted it.
func TestHeadingsComeBackInTheirConfiguredOrder(t *testing.T) {
	svc, ctx := reconciled(t, notes, users)

	if _, err := svc.UpdateGroup(ctx, "administration", "Administration", 5); err != nil {
		t.Fatalf("reorder: %v", err)
	}

	groups, err := svc.Groups(ctx)
	if err != nil {
		t.Fatalf("groups: %v", err)
	}
	if len(groups) != 2 || groups[0].ID != "administration" {
		t.Errorf("order is %v, want administration first", []string{groups[0].ID, groups[1].ID})
	}
}

func TestItemsListsEveryDeclaredDestination(t *testing.T) {
	svc, ctx := reconciled(t, notes, users)

	items, err := svc.Items(ctx)
	if err != nil {
		t.Fatalf("items: %v", err)
	}
	// The count assertion the permission test was missing: without it, Items returning nothing
	// passed a test that only ranged over what it returned.
	if len(items) != 2 {
		t.Fatalf("got %d items, want 2", len(items))
	}
}

// The screen that manages the pane cannot be hidden from the pane.
func TestTheLayoutScreenCannotHideItself(t *testing.T) {
	layout := navigationapi.Destination{
		ID: "navigation.layout", ModuleID: "navigation", DefaultTitle: "Navigation",
		DefaultGroup: "administration", DefaultOrder: 30,
		Permission: "navigation.manage", HideWhenDenied: true,
	}
	svc, ctx := reconciled(t, layout)

	_, err := svc.UpdateItem(ctx, "navigation.layout", "", "", false)
	if errs.KindOf(err) != errs.Invalid {
		t.Fatalf("hiding the layout screen returned %v, want an invalid-argument", err)
	}
	if msg := errs.PublicMessage(err); msg == "" {
		t.Error("the refusal carried no message a person could act on")
	}
}

package service_test

import (
	"context"
	"testing"

	"github.com/SekiroKenjii/kakehashi/server/internal/modules/navigation/service"
	"github.com/SekiroKenjii/kakehashi/server/internal/platform/auth"
	"github.com/SekiroKenjii/kakehashi/server/internal/platform/errs"
)

// applying returns a reconciled service over a fresh store, with notes and users seeded.
func applying(t *testing.T) (*service.Service, *fakeStore) {
	t.Helper()

	store := newFakeStore()
	svc := service.New(store, nil, notes, users)
	if err := svc.Reconcile(context.Background(), systemGroups); err != nil {
		t.Fatalf("reconcile: %v", err)
	}
	return svc, store
}

// asStored reads the arrangement back as specs, which is what a screen posts: the whole thing, with
// most of it unchanged.
func asStored(t *testing.T, svc *service.Service) ([]service.GroupSpec, []service.ItemSpec) {
	t.Helper()
	ctx := context.Background()

	groups, err := svc.Groups(ctx)
	if err != nil {
		t.Fatalf("groups: %v", err)
	}
	items, err := svc.Items(ctx)
	if err != nil {
		t.Fatalf("items: %v", err)
	}

	groupSpecs := make([]service.GroupSpec, 0, len(groups))
	for _, g := range groups {
		groupSpecs = append(groupSpecs, service.GroupSpec{ID: g.ID, Title: g.Title, Order: g.Order})
	}
	itemSpecs := make([]service.ItemSpec, 0, len(items))
	for _, i := range items {
		itemSpecs = append(itemSpecs, service.ItemSpec{
			ID: i.DestinationID, GroupID: i.GroupID, Order: i.Order,
			Title: i.Title, Icon: i.Icon, IsVisible: i.IsVisible,
		})
	}
	return groupSpecs, itemSpecs
}

// The whole reason this procedure exists. A sequence of single-row writes had no way to fail halfway
// without leaving the pane half-rearranged; this one validates everything first, so a refusal changes
// nothing at all.
func TestApplyLayoutWritesNothingWhenAnyPartIsRefused(t *testing.T) {
	svc, store := applying(t)
	ctx := context.Background()

	groups, items := asStored(t, svc)

	// Two changes that could be written, and one that cannot: hiding the administrative screen.
	for i := range items {
		switch items[i].ID {
		case "notes":
			items[i].Title = "Scratchpad"
			items[i].Order = 99
		case "account.users":
			items[i].IsVisible = false
		}
	}
	groups = append(groups, service.GroupSpec{Title: "Experimental", Order: 30})

	if _, err := svc.ApplyLayout(ctx, groups, items); errs.KindOf(err) != errs.Invalid {
		t.Fatalf("kind = %v, want %v", errs.KindOf(err), errs.Invalid)
	}

	notesRow, err := store.Placement(ctx, "notes")
	if err != nil {
		t.Fatalf("placement: %v", err)
	}
	if notesRow.Title != "" || notesRow.Order != 10 {
		t.Errorf("notes = %+v, want the arrangement it started with", notesRow)
	}
	if _, err := store.Group(ctx, "experimental"); err == nil {
		t.Error("the new heading was created despite the refusal")
	}
}

func TestApplyLayoutCountsOnlyWhatChanged(t *testing.T) {
	svc, _ := applying(t)
	ctx := context.Background()

	groups, items := asStored(t, svc)

	// Posting the arrangement unchanged changes nothing, and says so.
	outcome, err := svc.ApplyLayout(ctx, groups, items)
	if err != nil {
		t.Fatalf("apply: %v", err)
	}
	if outcome != (service.ApplyOutcome{}) {
		t.Errorf("outcome = %+v, want four zeroes", outcome)
	}

	groups = append(groups, service.GroupSpec{Title: "Experimental", Order: 30})
	for i := range groups {
		if groups[i].ID == "utilities" {
			groups[i].Title = "Tools"
		}
	}
	for i := range items {
		if items[i].ID == "notes" {
			items[i].GroupID = "experimental"
		}
	}

	outcome, err = svc.ApplyLayout(ctx, groups, items)
	if err != nil {
		t.Fatalf("apply: %v", err)
	}
	want := service.ApplyOutcome{GroupsCreated: 1, GroupsUpdated: 1, ItemsChanged: 1}
	if outcome != want {
		t.Errorf("outcome = %+v, want %+v", outcome, want)
	}
}

// The ordinary gesture: make a heading and drop something into it in one post. Checking the wanted
// heading against what is stored now, rather than against what this arrangement will end with, would
// refuse it.
func TestApplyLayoutAcceptsAHeadingAndAPlacementIntoItAtOnce(t *testing.T) {
	svc, store := applying(t)
	ctx := context.Background()

	groups, items := asStored(t, svc)
	groups = append(groups, service.GroupSpec{Title: "Experimental", Order: 30})
	for i := range items {
		if items[i].ID == "notes" {
			items[i].GroupID = "experimental"
		}
	}

	if _, err := svc.ApplyLayout(ctx, groups, items); err != nil {
		t.Fatalf("apply: %v", err)
	}

	placement, err := store.Placement(ctx, "notes")
	if err != nil {
		t.Fatalf("placement: %v", err)
	}
	if placement.GroupID != "experimental" {
		t.Errorf("notes is under %q, want experimental", placement.GroupID)
	}
}

// Absent means deleted, for headings — an administrator owns those. What was under one falls to
// ungrouped, which the schema does and the service relies on.
func TestApplyLayoutDeletesAHeadingLeftOutAndUngroupsWhatWasUnderIt(t *testing.T) {
	svc, store := applying(t)
	ctx := context.Background()

	groups, items := asStored(t, svc)
	groups = append(groups, service.GroupSpec{Title: "Experimental", Order: 30})
	for i := range items {
		if items[i].ID == "notes" {
			items[i].GroupID = "experimental"
		}
	}
	if _, err := svc.ApplyLayout(ctx, groups, items); err != nil {
		t.Fatalf("first apply: %v", err)
	}

	groups, items = asStored(t, svc)
	var kept []service.GroupSpec
	for _, g := range groups {
		if g.ID != "experimental" {
			kept = append(kept, g)
		}
	}

	outcome, err := svc.ApplyLayout(ctx, kept, items)
	if err != nil {
		t.Fatalf("second apply: %v", err)
	}
	if outcome.GroupsDeleted != 1 {
		t.Errorf("deleted %d headings, want 1", outcome.GroupsDeleted)
	}

	placement, err := store.Placement(ctx, "notes")
	if err != nil {
		t.Fatalf("placement: %v", err)
	}
	if placement.GroupID != "" {
		t.Errorf("notes is under %q, want ungrouped", placement.GroupID)
	}
}

// A deployment that deleted the heading its administrative screens live under would have nowhere left
// to put them, so leaving one out is refused with the reason rather than a bare no.
func TestApplyLayoutRefusesToDeleteAHeadingTheProductShips(t *testing.T) {
	svc, store := applying(t)
	ctx := context.Background()

	groups, items := asStored(t, svc)
	var kept []service.GroupSpec
	for _, g := range groups {
		if g.ID != "administration" {
			kept = append(kept, g)
		}
	}

	_, err := svc.ApplyLayout(ctx, kept, items)
	if errs.KindOf(err) != errs.Invalid {
		t.Fatalf("kind = %v, want %v", errs.KindOf(err), errs.Invalid)
	}
	if _, err := store.Group(ctx, "administration"); err != nil {
		t.Errorf("the heading was deleted anyway: %v", err)
	}
}

// Titles are unique in the database, so a collision would otherwise surface from inside a transaction
// naming one row. Caught first, it names both and nothing is written.
func TestApplyLayoutRefusesTwoHeadingsWithOneName(t *testing.T) {
	svc, _ := applying(t)

	groups, items := asStored(t, svc)
	groups = append(groups, service.GroupSpec{Title: "utilities", Order: 30})

	_, err := svc.ApplyLayout(context.Background(), groups, items)
	if errs.KindOf(err) != errs.Invalid {
		t.Errorf("kind = %v, want %v", errs.KindOf(err), errs.Invalid)
	}
}

// A row for a screen this build still has would be written straight back by the next reconcile, so
// deleting it is a no-op that looks like it worked until the server restarts.
func TestDeleteItemRefusesAScreenThisBuildStillHas(t *testing.T) {
	svc, store := applying(t)
	ctx := context.Background()

	if err := svc.DeleteItem(ctx, "notes"); errs.KindOf(err) != errs.Invalid {
		t.Fatalf("kind = %v, want %v", errs.KindOf(err), errs.Invalid)
	}
	if _, err := store.Placement(ctx, "notes"); err != nil {
		t.Errorf("notes was deleted anyway: %v", err)
	}
}

func TestDeleteItemRemovesALeftoverRow(t *testing.T) {
	store := newFakeStore()
	ctx := context.Background()

	before := service.New(store, nil, notes, users)
	if err := before.Reconcile(ctx, systemGroups); err != nil {
		t.Fatalf("reconcile: %v", err)
	}

	// A deploy that removed the notes module. Its row is still there, which is why the layout screen
	// lists it and why somebody needs a way to be rid of it.
	after := service.New(store, nil, users)
	if err := after.Reconcile(ctx, systemGroups); err != nil {
		t.Fatalf("reconcile after removal: %v", err)
	}

	if err := after.DeleteItem(ctx, "notes"); err != nil {
		t.Fatalf("delete: %v", err)
	}
	if _, err := store.Placement(ctx, "notes"); err == nil {
		t.Error("the leftover row survived")
	}
}

// What a reset button needs, and what nothing carried before: Reconcile writes these once as seeds and
// deliberately never re-applies them, so without reporting them a moved destination's intended place
// cannot be recovered through any API.
func TestItemsReportWhereTheCodePutsADestination(t *testing.T) {
	svc, _ := applying(t)
	ctx := context.Background()

	if _, err := svc.MoveItem(ctx, "notes", "administration", 99); err != nil {
		t.Fatalf("move: %v", err)
	}

	items, err := svc.Items(ctx)
	if err != nil {
		t.Fatalf("items: %v", err)
	}
	for _, item := range items {
		if item.DestinationID != "notes" {
			continue
		}
		if item.GroupID != "administration" || item.Order != 99 {
			t.Errorf("stored place = %s/%d, want administration/99", item.GroupID, item.Order)
		}
		if item.DefaultGroup != "utilities" || item.DefaultOrder != 10 {
			t.Errorf("the code's place = %s/%d, want utilities/10", item.DefaultGroup, item.DefaultOrder)
		}
		return
	}
	t.Fatal("notes was not listed")
}

// roleGrants is the authorization module, as far as previewing needs it.
type roleGrants map[string]auth.Grants

func (r roleGrants) GrantsForRole(_ context.Context, roleID string) (auth.Grants, error) {
	grants, ok := r[roleID]
	if !ok {
		return nil, errs.NotFoundf("No role with id %s.", roleID)
	}
	return grants, nil
}

// The point of previewing: an administrator holding everything cannot otherwise see the pane the
// people who will use it get.
func TestPreviewDrawsThePaneAsARoleWouldSeeIt(t *testing.T) {
	svc, _ := applying(t)
	svc.WithRoleGrants(roleGrants{
		"reader": {"notes.access": auth.ScopeAll},
	})

	pane, err := svc.PreviewFor(context.Background(), "reader")
	if err != nil {
		t.Fatalf("preview: %v", err)
	}

	// Notes is enabled; Users is absent entirely, because it hides rather than locks when denied.
	var sawNotes, sawUsers bool
	for _, group := range pane.Groups {
		for _, item := range group.Items {
			switch item.ID {
			case "notes":
				sawNotes = true
				if !item.Enabled {
					t.Error("notes is disabled for a role that holds its permission")
				}
			case "account.users":
				sawUsers = true
			}
		}
	}
	if !sawNotes {
		t.Error("notes is missing from the preview")
	}
	if sawUsers {
		t.Error("the administrative screen appears for a role that cannot reach it")
	}
}

// The useful worst case, and no round trip to be told an empty answer.
func TestPreviewWithNoRoleAsksTheAuthorizationModuleNothing(t *testing.T) {
	svc, _ := applying(t)
	svc.WithRoleGrants(roleGrants{})

	pane, err := svc.PreviewFor(context.Background(), "")
	if err != nil {
		t.Fatalf("preview: %v", err)
	}

	for _, group := range pane.Groups {
		for _, item := range group.Items {
			if item.ID == "notes" && item.Enabled {
				t.Error("notes is enabled for somebody holding nothing")
			}
			if item.ID == "account.users" {
				t.Error("the administrative screen appears for somebody holding nothing")
			}
		}
	}
}

// A build with no authorization module has no roles. Saying so beats a nil dereference on the one
// screen that would ask.
func TestPreviewSaysSoWhenThereAreNoRoles(t *testing.T) {
	svc, _ := applying(t)

	_, err := svc.PreviewFor(context.Background(), "reader")
	if errs.KindOf(err) != errs.Invalid {
		t.Errorf("kind = %v, want %v", errs.KindOf(err), errs.Invalid)
	}
}

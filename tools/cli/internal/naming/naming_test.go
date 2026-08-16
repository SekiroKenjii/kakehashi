package naming_test

import (
	"strings"
	"testing"

	"github.com/SekiroKenjii/kakehashi/tools/cli/internal/naming"
)

func TestNew(t *testing.T) {
	cases := []struct {
		id       string
		entity   string
		wantEnt  string
		wantMod  string
		wantVar  string
		wantIcon string
	}{
		{id: "orders", wantEnt: "Order", wantMod: "Orders", wantVar: "order", wantIcon: naming.DefaultIcon},
		{id: "notes", wantEnt: "Note", wantMod: "Notes", wantVar: "note", wantIcon: naming.DefaultIcon},
		{id: "categories", wantEnt: "Category", wantMod: "Categories", wantVar: "category", wantIcon: naming.DefaultIcon},
		{id: "boxes", wantEnt: "Box", wantMod: "Boxes", wantVar: "box", wantIcon: naming.DefaultIcon},
		{id: "inventory", wantEnt: "Inventory", wantMod: "Inventory", wantVar: "inventory", wantIcon: naming.DefaultIcon},
		// English has more rules than the three this derives, which is what --entity is for.
		{id: "people", entity: "Person", wantEnt: "Person", wantMod: "People", wantVar: "person", wantIcon: naming.DefaultIcon},
	}
	for _, c := range cases {
		t.Run(c.id, func(t *testing.T) {
			names, err := naming.New(c.id, c.entity, "")
			if err != nil {
				t.Fatalf("New: %v", err)
			}
			if names.Entity != c.wantEnt || names.Module != c.wantMod || names.Variable != c.wantVar {
				t.Errorf("New(%s) = %+v", c.id, names)
			}
			if names.Icon != c.wantIcon {
				t.Errorf("icon = %q, want %q", names.Icon, c.wantIcon)
			}
		})
	}
}

func TestNewRefusals(t *testing.T) {
	cases := []struct {
		name   string
		id     string
		entity string
		says   string
	}{
		{"a capital in the id", "Orders", "", "must match"},
		{"a dash in the id", "sales-orders", "", "must match"},
		{"a one-letter id", "o", "", "must match"},
		{"an id that is a layer name", "service", "", "reserved"},
		{"an id that is a module the template has", "account", "", "reserved"},
		{"an id that is a Go package the server has", "platform", "", "reserved"},
		{"an entity that is not PascalCase", "orders", "order", "must match"},
	}
	for _, c := range cases {
		t.Run(c.name, func(t *testing.T) {
			_, err := naming.New(c.id, c.entity, "")
			if err == nil {
				t.Fatalf("New accepted %s", c.name)
			}
			if !strings.Contains(err.Error(), c.says) {
				t.Errorf("error %q does not say %q", err, c.says)
			}
		})
	}
}

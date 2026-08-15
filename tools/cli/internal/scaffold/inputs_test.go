package scaffold

import (
	"strings"
	"testing"
	"time"
)

func base() Inputs {
	return Inputs{AppName: "OrderDesk", GoModule: "github.com/me/orderdesk"}
}

func TestDeriveFillsTheDefaults(t *testing.T) {
	in := base()
	in.Derive(time.Date(2026, 3, 4, 0, 0, 0, 0, time.UTC))

	want := Inputs{
		AppName:       "OrderDesk",
		AppTitle:      "OrderDesk",
		RootNamespace: "OrderDesk",
		GoModule:      "github.com/me/orderdesk",
		ProtoPackage:  "orderdesk",
		Accent:        DefaultAccent,
		Author:        "OrderDesk",
		Year:          "2026",
		Auth:          AuthInApp,
	}
	if in != want {
		t.Errorf("Derive:\n got %+v\nwant %+v", in, want)
	}
}

func TestDeriveKeepsWhatWasGiven(t *testing.T) {
	in := base()
	in.AppTitle = "Order Desk"
	in.ProtoPackage = "orders"
	in.RootNamespace = "Acme.OrderDesk"
	in.Accent = "#123456"
	in.Author = "Me"
	in.Year = "2019"
	in.Auth = AuthBrowser
	in.Derive(time.Now())

	if in.AppTitle != "Order Desk" || in.ProtoPackage != "orders" || in.RootNamespace != "Acme.OrderDesk" ||
		in.Accent != "#123456" || in.Author != "Me" || in.Year != "2019" || in.Auth != AuthBrowser {
		t.Errorf("Derive overwrote a value it was given: %+v", in)
	}
}

func TestValidate(t *testing.T) {
	cases := []struct {
		name    string
		mutate  func(*Inputs)
		refused bool
	}{
		{"the defaults", func(*Inputs) {}, false},
		{"a lower-case app name", func(in *Inputs) { in.AppName = "orderDesk" }, true},
		{"a one-letter app name", func(in *Inputs) { in.AppName = "O" }, true},
		{"an app name with a dot", func(in *Inputs) { in.AppName = "Order.Desk" }, true},
		{"a module path with a space", func(in *Inputs) { in.GoModule = "github.com/me/order desk" }, true},
		{"a module path that ends in a slash", func(in *Inputs) { in.GoModule = "github.com/me/orderdesk/" }, true},
		{"a proto package with a capital", func(in *Inputs) { in.ProtoPackage = "orderDesk" }, true},
		{"a proto package with a dash", func(in *Inputs) { in.ProtoPackage = "order-desk" }, true},
		{"a namespace that starts lower-case", func(in *Inputs) { in.RootNamespace = "orderDesk" }, true},
		{"a three-digit accent", func(in *Inputs) { in.Accent = "#E34" }, true},
		{"an accent with no hash", func(in *Inputs) { in.Accent = "E34234" }, true},
		{"a two-digit year", func(in *Inputs) { in.Year = "26" }, true},
		{"an empty title", func(in *Inputs) { in.AppTitle = "  " }, true},
		{"an empty author", func(in *Inputs) { in.Author = "" }, true},
		{"auth none, which is not built yet", func(in *Inputs) { in.Auth = AuthNone }, true},
		{"an auth mode that is not a mode", func(in *Inputs) { in.Auth = "saml" }, true},
		{"a title that is a placeholder", func(in *Inputs) { in.AppTitle = "__APP_TITLE__" }, true},
	}
	for _, c := range cases {
		t.Run(c.name, func(t *testing.T) {
			in := base()
			in.Derive(time.Now())
			c.mutate(&in)

			err := in.Validate()
			if c.refused && err == nil {
				t.Errorf("Validate accepted %s", c.name)
			}
			if !c.refused && err != nil {
				t.Errorf("Validate refused %s: %v", c.name, err)
			}
		})
	}
}

// __APP_NAME_LOWER__ starts with __APP_NAME_, so substituting the short name first would leave
// "OrderDeskLOWER__" behind.
func TestApplySubstitutesTheLongestPlaceholderFirst(t *testing.T) {
	in := base()
	in.Derive(time.Now())

	got := in.apply("__APP_NAME_LOWER__ __APP_NAME_UPPER__ __APP_NAME__")
	if got != "orderdesk ORDERDESK OrderDesk" {
		t.Errorf("apply = %q", got)
	}
	if strings.Contains(got, "_") {
		t.Errorf("apply left a placeholder fragment behind: %q", got)
	}
}

// Every placeholder the rename scripts know has to be in the table, or a scaffold fails its own
// self-check on a file the CLI never touched.
func TestTheTableCoversEveryPlaceholder(t *testing.T) {
	want := []string{
		"__APP_NAME__", "__APP_NAME_LOWER__", "__APP_NAME_UPPER__", "__APP_TITLE__",
		"__ROOT_NAMESPACE__", "__PROTO_PACKAGE__", "__GO_MODULE__", "__ACCENT__",
		"__AUTHOR__", "__YEAR__",
	}

	have := map[string]bool{}
	for _, r := range base().replacements() {
		have[r.name] = true
	}
	for _, name := range want {
		if !have[name] {
			t.Errorf("the substitution table is missing %s", name)
		}
	}
	if len(have) != len(want) {
		t.Errorf("the table has %d entries, the convention has %d", len(have), len(want))
	}
}

package marker_test

import (
	"strings"
	"testing"

	"github.com/SekiroKenjii/kakehashi/tools/cli/internal/marker"
)

// imports is the shape of the server's composition root: a sorted region holding bare lines and
// whole unit blocks side by side.
const imports = `import (
	"example.com/app/server/internal/app"
	// kakehashi:module-imports:begin
	"example.com/app/server/internal/modules/account"
	// kakehashi:unit-activity:begin
	"example.com/app/server/internal/modules/activity"
	// kakehashi:unit-activity:end
	"example.com/app/server/internal/modules/health"
	// kakehashi:unit-notes:begin
	"example.com/app/server/internal/modules/notes"
	// kakehashi:unit-notes:end
	// kakehashi:module-imports:end
	"example.com/app/server/internal/platform/logging"
)`

func goStyle(t *testing.T) marker.Style {
	t.Helper()
	style, err := marker.StyleFor("main.go")
	if err != nil {
		t.Fatal(err)
	}
	return style
}

func TestInsertSortsIntoTheRegion(t *testing.T) {
	got, err := marker.Insert(imports, marker.SectionImports, "orders",
		[]string{`"example.com/app/server/internal/modules/orders"`}, true, goStyle(t))
	if err != nil {
		t.Fatalf("Insert: %v", err)
	}

	lines := strings.Split(got, "\n")
	at := indexOf(t, lines, `"example.com/app/server/internal/modules/orders"`)
	notes := indexOf(t, lines, `"example.com/app/server/internal/modules/notes"`)
	logging := indexOf(t, lines, `"example.com/app/server/internal/platform/logging"`)

	// After notes, before the section ends — which is where an import path sorts, and gofmt is
	// what would object if it did not.
	if !(notes < at && at < logging) {
		t.Errorf("orders landed at %d, between notes (%d) and logging (%d):\n%s", at, notes, logging, got)
	}
	if lines[at-1] != "\t// kakehashi:unit-orders:begin" || lines[at+1] != "\t// kakehashi:unit-orders:end" {
		t.Errorf("the block is not fenced at the region's indentation:\n%s", strings.Join(lines[at-2:at+2], "\n"))
	}
}

// An insertion may never land inside another module's fence, however the keys sort.
func TestInsertNeverSplitsAnotherUnitsBlock(t *testing.T) {
	got, err := marker.Insert(imports, marker.SectionImports, "activityx",
		[]string{`"example.com/app/server/internal/modules/activityx"`}, true, goStyle(t))
	if err != nil {
		t.Fatalf("Insert: %v", err)
	}

	lines := strings.Split(got, "\n")
	begin := indexOf(t, lines, "// kakehashi:unit-activity:begin")
	end := indexOf(t, lines, "// kakehashi:unit-activity:end")
	at := indexOf(t, lines, `"example.com/app/server/internal/modules/activityx"`)
	if begin < at && at < end {
		t.Errorf("activityx was inserted inside the activity block:\n%s", got)
	}
}

func TestInsertAppendsWhenTheRegionIsNotSorted(t *testing.T) {
	const registrations = `	return []app.Module{
		// kakehashi:module-registrations:begin
		health.New(),
		// kakehashi:unit-notes:begin
		notes.New(),
		// kakehashi:unit-notes:end
		account.New(),
		// kakehashi:module-registrations:end
	}`

	got, err := marker.Insert(registrations, marker.SectionRegistrations, "orders",
		[]string{"orders.New(),"}, false, goStyle(t))
	if err != nil {
		t.Fatalf("Insert: %v", err)
	}

	lines := strings.Split(got, "\n")
	at := indexOf(t, lines, "orders.New(),")
	account := indexOf(t, lines, "account.New(),")
	end := indexOf(t, lines, "// kakehashi:module-registrations:end")
	if !(account < at && at < end) {
		t.Errorf("orders.New() landed at %d, want the end of the region (%d..%d):\n%s", at, account, end, got)
	}
}

// Running the generator twice has to say so rather than write the wiring a second time.
func TestInsertRefusesAUnitTheFileAlreadyWiresIn(t *testing.T) {
	_, err := marker.Insert(imports, marker.SectionImports, "notes",
		[]string{`"example.com/app/server/internal/modules/notes"`}, true, goStyle(t))
	if err == nil {
		t.Fatal("Insert wrote a second copy of an existing module's wiring")
	}
	if !strings.Contains(err.Error(), "notes") {
		t.Errorf("the refusal does not name the module: %v", err)
	}
}

func TestInsertRefusesAMissingSection(t *testing.T) {
	if _, err := marker.Insert(imports, marker.SectionIDs, "orders", []string{"x"}, true, goStyle(t)); err == nil {
		t.Error("Insert wrote into a section the file does not have")
	}
}

// What was inserted is what removal takes back, and nothing else.
func TestInsertAndStripRoundTrip(t *testing.T) {
	added, err := marker.Insert(imports, marker.SectionImports, "orders",
		[]string{`"example.com/app/server/internal/modules/orders"`}, true, goStyle(t))
	if err != nil {
		t.Fatalf("Insert: %v", err)
	}
	if !marker.Has(added, "orders") {
		t.Fatal("Has does not see the block Insert wrote")
	}

	back, err := marker.Strip(added, "orders")
	if err != nil {
		t.Fatalf("Strip: %v", err)
	}
	if back != imports {
		t.Errorf("round trip changed the file:\n got %q\nwant %q", back, imports)
	}
}

func TestStripTakesEveryBlockOfTheUnit(t *testing.T) {
	got, err := marker.Strip(imports, "notes")
	if err != nil {
		t.Fatalf("Strip: %v", err)
	}
	if strings.Contains(got, "notes") {
		t.Errorf("a mention of the unit survived:\n%s", got)
	}
	if !strings.Contains(got, "activity") {
		t.Errorf("another unit was taken with it:\n%s", got)
	}
}

func TestStripRefusals(t *testing.T) {
	cases := []struct {
		name string
		body string
	}{
		{"an unbalanced begin", "// kakehashi:unit-x:begin\nkept"},
		{"an end before its begin", "// kakehashi:unit-x:end\nkept\n// kakehashi:unit-x:begin"},
	}
	for _, c := range cases {
		t.Run(c.name, func(t *testing.T) {
			if _, err := marker.Strip(c.body, "x"); err == nil {
				t.Errorf("Strip accepted %s", c.name)
			}
		})
	}
}

// A marker has to be a comment in the file it lands in, and XML does not take a //.
func TestStyleFor(t *testing.T) {
	cases := []struct{ path, open, close string }{
		{"main.go", "//", ""},
		{"ModuleCatalog.cs", "//", ""},
		{"App.slnx", "<!--", "-->"},
		{"App.csproj", "<!--", "-->"},
		{"Page.xaml", "<!--", "-->"},
	}
	for _, c := range cases {
		style, err := marker.StyleFor(c.path)
		if err != nil {
			t.Fatalf("StyleFor(%s): %v", c.path, err)
		}
		if style.Open != c.open || style.Close != c.close {
			t.Errorf("StyleFor(%s) = %q %q, want %q %q", c.path, style.Open, style.Close, c.open, c.close)
		}
	}

	if _, err := marker.StyleFor("notes.txt"); err == nil {
		t.Error("StyleFor invented a comment syntax for a file type it does not know")
	}
}

func TestInsertIntoXml(t *testing.T) {
	const csproj = `  <ItemGroup>
    <!-- kakehashi:module-projects:begin -->
    <ProjectReference Include="Auth.csproj" />
    <!-- kakehashi:module-projects:end -->
  </ItemGroup>`

	style, err := marker.StyleFor("App.csproj")
	if err != nil {
		t.Fatal(err)
	}
	got, err := marker.Insert(csproj, marker.SectionProjects, "orders",
		[]string{`<ProjectReference Include="Orders.csproj" />`}, true, style)
	if err != nil {
		t.Fatalf("Insert: %v", err)
	}

	if !strings.Contains(got, `    <!-- kakehashi:unit-orders:begin -->`) {
		t.Errorf("the fence is not an XML comment at the region's indentation:\n%s", got)
	}
	back, err := marker.Strip(got, "orders")
	if err != nil || back != csproj {
		t.Errorf("round trip through XML changed the file: %v\n%s", err, back)
	}
}

func indexOf(t *testing.T, lines []string, want string) int {
	t.Helper()
	for i, line := range lines {
		if strings.TrimSpace(line) == strings.TrimSpace(want) {
			return i
		}
	}
	t.Fatalf("%q is not in:\n%s", want, strings.Join(lines, "\n"))
	return -1
}

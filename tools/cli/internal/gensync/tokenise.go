package gensync

import (
	"regexp"
	"strings"
)

// rule is one substitution, applied in order. Everything is a regular expression so that a rule
// which has to look at what surrounds a name can be written the same way as one that does not.
type rule struct {
	find    *regexp.Regexp
	replace string
}

// rules turn the example module's text into a template. Order is the whole of the correctness
// here: the plural forms are replaced before the singular ones they contain, and the two rules
// that mean something other than the entity run before either.
var rules = []rule{
	// The project's own identity, which the template repository already spells as placeholders.
	literal("__APP_NAME_LOWER__", "{{.AppNameLower}}"),
	literal("__APP_NAME_UPPER__", "{{.AppNameUpper}}"),
	literal("__APP_NAME__", "{{.AppName}}"),
	literal("__APP_TITLE__", "{{.AppTitle}}"),
	literal("__ROOT_NAMESPACE__", "{{.RootNamespace}}"),
	literal("__PROTO_PACKAGE__", "{{.ProtoPackage}}"),
	literal("__GO_MODULE__", "{{.GoModule}}"),
	literal("__ACCENT__", "{{.Accent}}"),
	literal("__AUTHOR__", "{{.Author}}"),
	literal("__YEAR__", "{{.Year}}"),

	// The navigation icon is a name from the client's own vocabulary, not the entity's name: an
	// icon called "order" is one the font cannot draw (docs/adr/0013).
	{regexp.MustCompile(`(DefaultIcon:(?:\s*))"note"`), `$1"{{.Icon}}"`},

	// The client draws a glyph rather than resolving the semantic name, so a module generated from
	// the example would otherwise be drawn with the example's icon.
	{regexp.MustCompile(`("\\uE70B")`), `"{{.Glyph}}"`},

	// A heading, shouted. The generic rules below would leave it as the plural in title case.
	literal("NOTES", "{{upper .Module}}"),

	// English, before the name it belongs to: "a note" has to become "an order" and not "a order".
	// The article is rewritten and the name left for the rules below to take.
	{regexp.MustCompile(`\ba (note)\b`), `{{article .Variable}} $1`},
	{regexp.MustCompile(`\bA (note)\b`), `{{articleUpper .Variable}} $1`},

	// A C# interpolated string puts a brace against the name — $"{NoteDraft.MaxTitleLength}" — and
	// the substitution below would turn that brace and the one opening the action into the {{ that
	// starts a template action. The brace becomes a literal, and the name is left for the rules
	// below to take.
	{regexp.MustCompile(`\{(Notes|notes|Note|note)`), `{{"{"}}$1`},

	// The names themselves. Plain substitution rather than word-bounded, because every compound
	// the module builds — NotesService, notesapi, GrpcNotesGateway, notesv1connect — is the name
	// with something joined onto it, and a word boundary is exactly what those do not have.
	literal("Notes", "{{.Module}}"),
	literal("notes", "{{.ID}}"),
	literal("Note", "{{.Entity}}"),
	literal("note", "{{.Variable}}"),
}

func literal(find, replace string) rule {
	return rule{find: regexp.MustCompile(regexp.QuoteMeta(find)), replace: replace}
}

// tokenise applies every rule to a string, which is a file's content or a file's path.
func tokenise(s string) string {
	for _, r := range rules {
		s = r.find.ReplaceAllString(s, r.replace)
	}
	return s
}

// Untokenised reports the module names a derived template still spells out. It is the check that a
// rule was not forgotten: nothing in a template may name the example module, because a generated
// module would then carry that name into a project.
func Untokenised(body string) []string {
	var found []string
	for _, name := range []string{"Notes", "notes", "Note", "note", "NOTES"} {
		if strings.Contains(body, name) {
			found = append(found, name)
		}
	}
	return found
}

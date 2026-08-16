package tui

import (
	"errors"
	"fmt"
	"strings"
	"time"

	"github.com/charmbracelet/huh"

	"github.com/SekiroKenjii/kakehashi/tools/cli/internal/scaffold"
)

// The two answers the accent question has. Custom opens the follow-up question; vermilion is the
// template's own and needs none.
const (
	accentVermilion = "vermilion"
	accentCustom    = "custom"
)

// Options is what the wizard needs from the command that opens it.
type Options struct {
	// Author is the default the command already read out of git config. It is not a question:
	// docs/pivot/05-PHASE-4-UI.md §1 asks that derivable things not be asked for.
	Author string

	// Destination renders where a project of the given name would be written, for the summary to
	// show. Where that is stays the command's rule.
	Destination func(appName string) string
}

// Wizard asks the seven questions of docs/pivot/05-PHASE-4-UI.md §1 and returns what they answered,
// underived: the command derives and validates every path the same way. Every question but the app
// name has a default, so Enter through the rest produces a project.
func Wizard(opts Options) (scaffold.Inputs, error) {
	if !interactive() {
		return scaffold.Inputs{}, ErrNoTTY
	}

	a := newAnswers()
	form := huh.NewForm(a.groups(opts)...).
		WithTheme(theme()).
		WithShowHelp(true)

	if err := form.Run(); err != nil {
		if errors.Is(err, huh.ErrUserAborted) {
			return scaffold.Inputs{}, ErrCancelled
		}
		return scaffold.Inputs{}, err
	}
	return a.inputs(opts.Author), nil
}

// answers is what the seven questions write into. The fields are exported because huh recomputes a
// dynamic description by hashing the value it was bound to, and reflection over a struct reaches
// only its exported fields — an unexported one leaves the summary showing the first answer forever.
//
// AppTitle and GoModule stay empty until somebody types one. Empty means "the derived default",
// which is what the placeholder is already showing, so Enter accepts what the reader can see.
type answers struct {
	AppName     string
	AppTitle    string
	GoModule    string
	WithExample bool
	Auth        string
	AccentKind  string
	AccentHex   string
}

func newAnswers() *answers {
	return &answers{
		AppName:     suggestedAppName(),
		WithExample: true,
		Auth:        scaffold.AuthInApp,
		AccentKind:  accentVermilion,
		AccentHex:   scaffold.DefaultAccent,
	}
}

// groups is one question per screen, in the order of the spec. Each is its own group so the wizard
// pages rather than presenting seven fields at once, and so going back re-asks exactly one thing.
func (a *answers) groups(opts Options) []*huh.Group {
	return []*huh.Group{
		huh.NewGroup(a.appNameField()),
		huh.NewGroup(a.titleField()),
		huh.NewGroup(a.goModuleField()),
		huh.NewGroup(a.exampleField()),
		huh.NewGroup(a.authField()),
		huh.NewGroup(a.accentField()),
		huh.NewGroup(a.accentHexField()).
			WithHideFunc(func() bool { return a.AccentKind != accentCustom }),
		huh.NewGroup(a.confirmField(opts)),
	}
}

func (a *answers) appNameField() huh.Field {
	return huh.NewInput().
		Title("App name").
		Description("PascalCase. It becomes the C# root namespace, the .slnx name and every\n" +
			"project name.").
		Placeholder("OrderDesk").
		Value(&a.AppName).
		Validate(scaffold.ValidateAppName)
}

func (a *answers) titleField() huh.Field {
	return huh.NewInput().
		Title("Display title").
		Description("What the window and the Home page call it. Enter accepts the default.").
		PlaceholderFunc(func() string { return defaultTitle(a.AppName) }, &a.AppName).
		Value(&a.AppTitle).
		Validate(optional(scaffold.ValidateTitle))
}

func (a *answers) goModuleField() huh.Field {
	return huh.NewInput().
		Title("Go module path").
		Description("The server's module path. Change it before the first push, not after.").
		PlaceholderFunc(func() string { return defaultGoModule(a.AppName) }, &a.AppName).
		Value(&a.GoModule).
		Validate(optional(scaffold.ValidateGoModule))
}

func (a *answers) exampleField() huh.Field {
	return huh.NewConfirm().
		Title("Include the Notes example module?").
		Description("One feature end to end across both halves: proto, server module, client\n" +
			"module, one page. Remove it later with `kakehashi remove module notes`.").
		Affirmative("Yes").
		Negative("No, bare").
		Value(&a.WithExample)
}

func (a *answers) authField() huh.Field {
	return huh.NewSelect[string]().
		Title("Sign-in mode").
		Description("Both talk to the server's own OpenID Connect provider. They differ in where\n"+
			"the password is typed.").
		Options(
			huh.NewOption("In-app — a page inside the window", scaffold.AuthInApp),
			huh.NewOption("System browser — the default browser, then back", scaffold.AuthBrowser),
		).
		Value(&a.Auth)
}

func (a *answers) accentField() huh.Field {
	return huh.NewSelect[string]().
		Title("Accent colour").
		Description("One value. Every themed brush in the client is derived from it.").
		Options(
			huh.NewOption("Vermilion "+scaffold.DefaultAccent, accentVermilion),
			huh.NewOption("Custom hex", accentCustom),
		).
		Value(&a.AccentKind)
}

func (a *answers) accentHexField() huh.Field {
	return huh.NewInput().
		Title("Accent hex").
		Description("Six hexadecimal digits after a hash.").
		Placeholder(scaffold.DefaultAccent).
		Value(&a.AccentHex).
		Validate(scaffold.ValidateAccent)
}

// confirmField is the summary. It is a note rather than a question because there is nothing left to
// answer: the choices are to run it, or to go back and change one of the seven.
func (a *answers) confirmField(opts Options) huh.Field {
	return huh.NewNote().
		Title("Ready").
		DescriptionFunc(func() string { return a.summary(opts) }, a).
		Next(true).
		NextLabel("Create")
}

// inputs is the answers as the scaffold wants them: trimmed, with each blank answer replaced by the
// default its placeholder was showing.
func (a *answers) inputs(author string) scaffold.Inputs {
	name := strings.TrimSpace(a.AppName)
	in := scaffold.Inputs{
		AppName:     name,
		AppTitle:    strings.TrimSpace(a.AppTitle),
		GoModule:    strings.TrimSpace(a.GoModule),
		Accent:      a.accent(),
		Author:      author,
		Auth:        a.Auth,
		WithExample: a.WithExample,
	}
	if in.AppTitle == "" {
		in.AppTitle = defaultTitle(name)
	}
	if in.GoModule == "" {
		in.GoModule = defaultGoModule(name)
	}
	return in
}

func (a *answers) accent() string {
	if a.AccentKind == accentCustom {
		return strings.TrimSpace(a.AccentHex)
	}
	return scaffold.DefaultAccent
}

// optional turns a rule into one that accepts a blank answer, which the wizard reads as "the
// default the placeholder is showing" rather than as an empty value.
func optional(rule func(string) error) func(string) error {
	return func(value string) error {
		if strings.TrimSpace(value) == "" {
			return nil
		}
		return rule(value)
	}
}

// summary is the table the last screen shows: what was answered above the line, what was derived
// from it below, and where it all lands.
func (a *answers) summary(opts Options) string {
	in := a.inputs(opts.Author)
	in.Derive(time.Now())

	example := "no — bare"
	if in.WithExample {
		example = "yes — Notes"
	}
	sign := "in-app"
	if in.Auth == scaffold.AuthBrowser {
		sign = "system browser"
	}

	rows := [][2]string{
		{"App name", in.AppName},
		{"Display title", in.AppTitle},
		{"Go module", in.GoModule},
		{"Example module", example},
		{"Sign-in", sign},
		{"Accent", in.Accent},
		{"", ""},
		{"Root namespace", in.RootNamespace},
		{"Proto package", in.ProtoPackage},
		{"Author, year", in.Author + ", " + in.Year},
		{"Destination", opts.destination(in.AppName)},
	}

	var out strings.Builder
	for _, row := range rows {
		if row[0] == "" {
			out.WriteString("\n")
			continue
		}
		fmt.Fprintf(&out, "%-16s %s\n", row[0], row[1])
	}
	return strings.TrimRight(out.String(), "\n")
}

// destination is where the project would go, or a placeholder when the command did not say how to
// work that out. The wizard shows the answer; it does not decide it.
func (opts Options) destination(appName string) string {
	if opts.Destination == nil {
		return "./" + strings.ToLower(appName)
	}
	return opts.Destination(appName)
}

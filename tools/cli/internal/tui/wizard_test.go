package tui

import (
	"errors"
	"strings"
	"testing"
	"time"

	"github.com/SekiroKenjii/kakehashi/tools/cli/internal/scaffold"
)

// answered is the wizard as somebody left it after typing only the app name, which is the path
// "Enter through the defaults" takes.
func answered(name string) *answers {
	a := newAnswers()
	a.AppName = name
	return a
}

func TestInputsFillInEveryBlankAnswer(t *testing.T) {
	in := answered("OrderDesk").inputs("Me")

	if in.AppTitle != "Order Desk" {
		t.Errorf("AppTitle = %q, want the title derived from the name", in.AppTitle)
	}
	if !strings.HasSuffix(in.GoModule, "/orderdesk") {
		t.Errorf("GoModule = %q, want one ending in the lower-case name", in.GoModule)
	}
	if in.Accent != scaffold.DefaultAccent || in.Auth != scaffold.AuthInApp || !in.WithExample {
		t.Errorf("defaults = %+v", in)
	}
	if in.Author != "Me" {
		t.Errorf("Author = %q, want the one the command read out of git", in.Author)
	}
}

func TestInputsAreValidWithoutFurtherAnswers(t *testing.T) {
	in := answered("OrderDesk").inputs("Me")
	in.Derive(time.Now())

	if err := in.Validate(); err != nil {
		t.Fatalf("a wizard answered with defaults produced invalid inputs: %v", err)
	}
}

func TestCustomAccentIsOnlyUsedWhenItWasChosen(t *testing.T) {
	a := answered("OrderDesk")
	a.AccentHex = "#00FF00"

	if got := a.accent(); got != scaffold.DefaultAccent {
		t.Errorf("accent = %q while the answer was vermilion, want %q", got, scaffold.DefaultAccent)
	}

	a.AccentKind = accentCustom
	if got := a.accent(); got != "#00FF00" {
		t.Errorf("accent = %q after choosing custom, want the hex that was typed", got)
	}
}

func TestSummaryShowsWhatWasDerivedAsWellAsWhatWasAnswered(t *testing.T) {
	a := answered("OrderDesk")
	a.WithExample = false
	a.Auth = scaffold.AuthBrowser

	summary := a.summary(Options{
		Author:      "Me",
		Destination: func(appName string) string { return "./" + strings.ToLower(appName) },
	})

	for _, want := range []string{
		"OrderDesk", "Order Desk", "orderdesk", "bare", "system browser",
		scaffold.DefaultAccent, "./orderdesk", "Me",
	} {
		if !strings.Contains(summary, want) {
			t.Errorf("summary does not mention %q:\n%s", want, summary)
		}
	}
}

func TestOptionalAcceptsABlankAnswerAndNothingElse(t *testing.T) {
	rule := optional(scaffold.ValidateGoModule)

	if err := rule("   "); err != nil {
		t.Errorf("a blank answer was refused: %v", err)
	}
	if err := rule("not a module path"); err == nil {
		t.Error("a bad answer was accepted")
	}
}

// A test process has its input and output on pipes, which is exactly the terminal the wizard has to
// refuse. That makes the refusal the one part of Wizard a test can reach.
func TestWizardRefusesATerminalThatCannotPrompt(t *testing.T) {
	_, err := Wizard(Options{})

	if !errors.Is(err, ErrNoTTY) {
		t.Fatalf("Wizard err = %v, want ErrNoTTY", err)
	}
}

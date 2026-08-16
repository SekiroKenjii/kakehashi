package tui

import (
	"github.com/charmbracelet/huh"
	"github.com/charmbracelet/lipgloss"

	"github.com/SekiroKenjii/kakehashi/tools/cli/internal/scaffold"
)

// accent is the template's own vermilion, and the only colour this wizard introduces. Everything
// else is the terminal's palette by number, so the wizard reads on a light scheme and a dark one
// without asking which it is on.
var accent = lipgloss.Color(scaffold.DefaultAccent)

// muted is dimmed body text: descriptions, the parts of the summary that are derived rather than
// answered, and the frame around the focused field.
var muted = lipgloss.AdaptiveColor{Light: "245", Dark: "243"}

// theme dresses huh in the accent. It starts from ThemeBase rather than ThemeCharm because
// ThemeCharm is indigo and fuchsia, which is the look docs/pivot/05-PHASE-4-UI.md §2.3 rules out.
func theme() *huh.Theme {
	t := huh.ThemeBase()

	t.Focused.Base = t.Focused.Base.BorderForeground(accent)
	t.Focused.Card = t.Focused.Base
	t.Focused.Title = t.Focused.Title.Foreground(accent).Bold(true)
	t.Focused.NoteTitle = t.Focused.NoteTitle.Foreground(accent).Bold(true).MarginBottom(1)
	t.Focused.Description = t.Focused.Description.Foreground(muted)
	t.Focused.ErrorIndicator = t.Focused.ErrorIndicator.Foreground(errorColour)
	t.Focused.ErrorMessage = t.Focused.ErrorMessage.Foreground(errorColour)
	t.Focused.SelectSelector = t.Focused.SelectSelector.Foreground(accent)
	t.Focused.NextIndicator = t.Focused.NextIndicator.Foreground(accent)
	t.Focused.PrevIndicator = t.Focused.PrevIndicator.Foreground(accent)
	t.Focused.SelectedOption = t.Focused.SelectedOption.Foreground(accent)
	t.Focused.FocusedButton = t.Focused.FocusedButton.Foreground(onAccent).Background(accent)
	t.Focused.Next = t.Focused.FocusedButton
	t.Focused.TextInput.Prompt = t.Focused.TextInput.Prompt.Foreground(accent)
	t.Focused.TextInput.Cursor = t.Focused.TextInput.Cursor.Foreground(accent)
	t.Focused.TextInput.Placeholder = t.Focused.TextInput.Placeholder.Foreground(muted)

	// Blurred is Focused with the frame hidden, which is how huh's own themes keep a form from
	// jumping sideways as focus moves.
	t.Blurred = t.Focused
	t.Blurred.Base = t.Focused.Base.BorderStyle(lipgloss.HiddenBorder())
	t.Blurred.Card = t.Blurred.Base
	t.Blurred.NextIndicator = lipgloss.NewStyle()
	t.Blurred.PrevIndicator = lipgloss.NewStyle()

	t.Group.Title = t.Focused.Title
	t.Group.Description = t.Focused.Description
	return t
}

// The two colours the accent does not cover: a refusal, and text drawn on top of the accent.
var (
	errorColour = lipgloss.AdaptiveColor{Light: "#B3261E", Dark: "#F2B8B5"}
	onAccent    = lipgloss.Color("#FFFFFF")
)

// The styles the run report uses. They are plain weights and the accent — no background fills, no
// box drawing: the report is copied out of a terminal as often as it is read in one.
var (
	tickStyle  = lipgloss.NewStyle().Foreground(accent).Bold(true)
	stepStyle  = lipgloss.NewStyle().Foreground(muted)
	titleStyle = lipgloss.NewStyle().Bold(true)
	pathStyle  = lipgloss.NewStyle().Foreground(accent)
)

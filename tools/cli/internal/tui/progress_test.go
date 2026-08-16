package tui

import (
	"bytes"
	"strings"
	"testing"

	"github.com/SekiroKenjii/kakehashi/tools/cli/internal/scaffold"
	"github.com/SekiroKenjii/kakehashi/tools/cli/internal/template"
)

// lines is the rendered output split into the lines a reader sees, with the styling stripped down
// to the words: a test that asserts on escape sequences asserts on the terminal, not on the code.
func lines(out string) []string {
	var got []string
	for _, line := range strings.Split(out, "\n") {
		if trimmed := strings.TrimSpace(stripANSI(line)); trimmed != "" {
			got = append(got, trimmed)
		}
	}
	return got
}

func stripANSI(s string) string {
	var out strings.Builder
	for i := 0; i < len(s); i++ {
		if s[i] == 0x1b {
			for i < len(s) && s[i] != 'm' {
				i++
			}
			continue
		}
		out.WriteByte(s[i])
	}
	return out.String()
}

func TestProgressTicksEachStepOnceItIsOver(t *testing.T) {
	var out bytes.Buffer
	p := NewProgress(&out)

	// The order the pipeline reports, named by the packages that perform the steps.
	for _, step := range []string{
		template.StepFetch, template.StepVerify,
		scaffold.StepApply, scaffold.StepCheck, scaffold.StepGit,
	} {
		p.Step(step)
	}
	p.Done()

	want := []string{"✓ fetch", "✓ verify", "✓ apply", "✓ check", "✓ git"}
	got := lines(out.String())
	if len(got) != len(want) {
		t.Fatalf("progress printed %d lines, want %d:\n%s", len(got), len(want), out.String())
	}
	for i, line := range want {
		if got[i] != line {
			t.Errorf("line %d = %q, want %q", i, got[i], line)
		}
	}
}

// A step is printed when it ends, so a step that is still running has printed nothing — which is
// what keeps a failed run from claiming the step it died in.
func TestProgressSaysNothingAboutTheStepStillRunning(t *testing.T) {
	var out bytes.Buffer
	p := NewProgress(&out)
	p.Step(template.StepFetch)

	if out.Len() != 0 {
		t.Errorf("progress announced a step before it finished: %q", out.String())
	}
}

func TestDoneIsSafeWithNoStepOpen(t *testing.T) {
	var out bytes.Buffer
	p := NewProgress(&out)
	p.Done()
	p.Done()

	if out.Len() != 0 {
		t.Errorf("Done printed a tick for a step that never ran: %q", out.String())
	}
}

func TestNextStepsIsAPasteableSequence(t *testing.T) {
	var out bytes.Buffer
	NextSteps(&out, Summary{
		Title: "Order Desk", AppName: "OrderDesk", Dir: "orderdesk",
		WithExample: true, Committed: true,
	})

	body := stripANSI(out.String())
	for _, want := range []string{
		"cd orderdesk",
		"docker compose up -d",
		"curl http://localhost:8080/healthz",
		`dotnet run --project "client/src/App/OrderDesk.App/OrderDesk.App.csproj" -p:Platform=x64`,
		"kakehashi add module orders",
		"kakehashi remove module notes",
		"docs/getting-started.md",
	} {
		if !strings.Contains(body, want) {
			t.Errorf("next steps do not mention %q:\n%s", want, body)
		}
	}
}

func TestNextStepsLeavesOutWhatDoesNotApply(t *testing.T) {
	var out bytes.Buffer
	NextSteps(&out, Summary{
		Title: "Order Desk", AppName: "OrderDesk", Dir: "orderdesk",
		WithExample: false, Committed: false,
	})

	body := stripANSI(out.String())
	if strings.Contains(body, "remove module notes") {
		t.Error("a bare project was told to remove an example it does not have")
	}
	if !strings.Contains(body, "git init") {
		t.Error("an uncommitted project was not told how to become a repository")
	}
}

// The refusal is the whole of what a CI runner gets, so it has to carry the command that works
// there rather than an apology.
func TestRefusalNamesTheFlagsThatReplaceTheQuestions(t *testing.T) {
	refusal := Refusal("")

	for _, want := range []string{"kakehashi new", "--module", "--no-input", "--bare"} {
		if !strings.Contains(refusal, want) {
			t.Errorf("refusal does not mention %q:\n%s", want, refusal)
		}
	}
}

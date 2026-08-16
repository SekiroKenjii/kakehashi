package tui

import (
	"fmt"
	"io"
	"strings"
)

// Progress prints a tick per pipeline step. A step is printed when it finishes, not when it starts:
// the packages doing the work already say what they are doing, and a line that has to be rewritten
// to become a tick is a line that is wrong in a log file.
type Progress struct {
	out  io.Writer
	open string
}

// NewProgress writes progress to out.
func NewProgress(out io.Writer) *Progress { return &Progress{out: out} }

// Step closes whichever step was running and opens the named one.
func (p *Progress) Step(name string) {
	p.Done()
	p.open = name
}

// Done closes the step that was running. It is safe to call when none is.
func (p *Progress) Done() {
	if p.open == "" {
		return
	}
	fmt.Fprintf(p.out, "  %s %s\n", tickStyle.Render("✓"), stepStyle.Render(p.open))
	p.open = ""
}

// Summary is a finished scaffold, as the closing block describes it.
type Summary struct {
	Title       string
	AppName     string
	Dir         string
	WithExample bool
	Committed   bool
}

// NextSteps prints the closing block: the commands that take the project from scaffolded to
// running, in the order they are run, each on its own line so the whole block can be pasted.
func NextSteps(out io.Writer, s Summary) {
	fmt.Fprintf(out, "\n  %s is ready in %s\n\n%s\n",
		titleStyle.Render(s.Title), pathStyle.Render(s.Dir), titleStyle.Render("  Next steps"))

	commands := []string{
		"cd " + s.Dir,
		"docker compose up -d",
		"curl http://localhost:8080/healthz",
		fmt.Sprintf("dotnet run --project \"client/src/App/%s.App/%s.App.csproj\" -p:Platform=x64",
			s.AppName, s.AppName),
	}
	if !s.Committed {
		commands = append(commands, "git init -b main && git add -A && git commit -m \"chore: scaffold\"")
	}
	for _, command := range commands {
		fmt.Fprintf(out, "    %s\n", command)
	}

	fmt.Fprintf(out, "\n%s\n", titleStyle.Render("  Then"))
	then := []string{
		"kakehashi add module orders    a module across both halves, all three gates green",
	}
	if s.WithExample {
		then = append(then, "kakehashi remove module notes  take the example back out")
	}
	then = append(then, "docs/getting-started.md        the first five minutes, in writing")
	for _, line := range then {
		fmt.Fprintf(out, "    %s\n", line)
	}

	if !s.Committed {
		fmt.Fprintf(out, "\n  %s\n",
			stepStyle.Render("git was not available, so the project is not a repository yet"))
	}
}

// Refusal is what a terminal that cannot prompt is told instead of being asked. It names the flags
// that answer the same questions, so the reader's next command is on the screen already.
func Refusal(appName string) string {
	if appName == "" {
		appName = "OrderDesk"
	}
	return strings.Join([]string{
		"this terminal cannot prompt, so the wizard cannot open",
		"",
		"    kakehashi new " + appName + " --module github.com/you/" + strings.ToLower(appName) + " --no-input",
		"",
		"  --title, --accent, --auth and --bare answer the rest; kakehashi new --help lists them all",
	}, "\n")
}

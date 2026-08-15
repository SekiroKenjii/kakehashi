// Package checks probes a machine for what building a scaffolded project needs. Every check
// answers with a status, what it found, and the one command that fixes it — a report a reader
// cannot act on is a report that wastes the run it took.
package checks

import (
	"context"
	"os/exec"
	"runtime"
	"strings"
	"time"
)

// Level separates what a project cannot be built without from what only some workflows need.
type Level string

// The levels, as they appear in --json.
const (
	Required Level = "required"
	Advisory Level = "advisory"
)

// Status is the outcome of one probe.
type Status string

// The statuses. Skip is for a check that does not apply to this operating system, which is not the
// same as one that failed.
const (
	Pass Status = "pass"
	Warn Status = "warn"
	Fail Status = "fail"
	Skip Status = "skip"
)

// probeTimeout bounds one probe. A docker daemon that is starting up, or a network that is
// dropping packets, must not hold the whole report.
const probeTimeout = 20 * time.Second

// Check is one thing the machine either has or does not.
type Check struct {
	Name     string
	Level    Level
	Windows  bool
	probe    func(ctx context.Context) (Status, string, string)
	scaffold bool
}

// Result is what a check found, and is the shape `doctor --json` prints.
type Result struct {
	Name   string `json:"name"`
	Level  Level  `json:"level"`
	Status Status `json:"status"`
	Detail string `json:"detail"`
	Fix    string `json:"fix,omitempty"`
}

// All is the full table, in the order docs/pivot/03-PHASE-2-CLI.md §3 lists it.
func All() []Check {
	return []Check{
		goToolchain(),
		dotnetSDK(),
		bufCLI(),
		protocPlugins(),
		dockerDaemon(),
		windowsAppRuntime(),
		gitCLI(),
		developerMode(),
		gitHubReachable(),
	}
}

// ForScaffold is the subset `new` runs before it writes anything: what scaffolding itself needs,
// which is not what building the result needs. The .NET SDK belongs in the second list and not in
// this one — a server-only workflow on Linux scaffolds perfectly well without it.
func ForScaffold() []Check {
	var subset []Check
	for _, check := range All() {
		if check.scaffold {
			subset = append(subset, check)
		}
	}
	return subset
}

// Run probes every check, skipping the ones this operating system cannot answer.
func Run(ctx context.Context, checks []Check) []Result {
	results := make([]Result, 0, len(checks))
	for _, check := range checks {
		result := Result{Name: check.Name, Level: check.Level}
		switch {
		case check.Windows && runtime.GOOS != "windows":
			result.Status, result.Detail = Skip, "Windows only"
		default:
			probe, cancel := context.WithTimeout(ctx, probeTimeout)
			result.Status, result.Detail, result.Fix = check.probe(probe)
			cancel()
		}
		results = append(results, result)
	}
	return results
}

// Failed lists the required checks that did not pass, which is what `new` refuses to start on.
func Failed(results []Result) []Result {
	var failed []Result
	for _, result := range results {
		if result.Level == Required && result.Status == Fail {
			failed = append(failed, result)
		}
	}
	return failed
}

// output runs a probe command and returns its combined output on one line, which is all any
// version banner is worth.
func output(ctx context.Context, name string, args ...string) (string, error) {
	out, err := exec.CommandContext(ctx, name, args...).CombinedOutput()
	return strings.TrimSpace(string(out)), err
}

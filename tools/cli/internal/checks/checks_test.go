package checks

import (
	"context"
	"runtime"
	"testing"
)

func TestRunSkipsWhatThisOperatingSystemCannotAnswer(t *testing.T) {
	if runtime.GOOS == "windows" {
		t.Skip("the Windows checks apply here")
	}

	for _, result := range Run(context.Background(), All()) {
		if result.Status == Skip && result.Level == Required {
			t.Errorf("%s is required and was skipped, so nothing checks it anywhere", result.Name)
		}
	}
}

func TestRunAnswersEveryCheck(t *testing.T) {
	results := Run(context.Background(), All())
	if len(results) != len(All()) {
		t.Fatalf("%d results for %d checks", len(results), len(All()))
	}

	for _, result := range results {
		if result.Name == "" || result.Status == "" || result.Detail == "" {
			t.Errorf("incomplete result: %+v", result)
		}
		if result.Status == Fail && result.Fix == "" {
			t.Errorf("%s failed without saying how to fix it", result.Name)
		}
	}
}

// Everything `new` runs before it writes has to be something it can refuse on, which means every
// one of them is required.
func TestForScaffoldIsARequiredSubset(t *testing.T) {
	subset := ForScaffold()
	if len(subset) == 0 || len(subset) >= len(All()) {
		t.Fatalf("the scaffold subset has %d of %d checks", len(subset), len(All()))
	}

	for _, check := range subset {
		if check.Level != Required {
			t.Errorf("%s is in the scaffold subset but is only advisory", check.Name)
		}
		if check.Windows {
			t.Errorf("%s is in the scaffold subset but only answers on Windows", check.Name)
		}
	}
}

func TestFailedIgnoresAdvisoryWarnings(t *testing.T) {
	results := []Result{
		{Name: "required, passing", Level: Required, Status: Pass},
		{Name: "required, failing", Level: Required, Status: Fail},
		{Name: "advisory, warning", Level: Advisory, Status: Warn},
		{Name: "advisory, failing", Level: Advisory, Status: Fail},
	}

	failed := Failed(results)
	if len(failed) != 1 || failed[0].Name != "required, failing" {
		t.Errorf("Failed = %+v", failed)
	}
}

func TestAtLeast(t *testing.T) {
	cases := []struct {
		banner string
		want   Status
	}{
		{"go version go1.26.0 linux/amd64", Pass},
		{"go version go1.27.3 linux/amd64", Pass},
		{"go version go1.25.13 linux/amd64", Fail},
		{"go version go2.0.0 linux/amd64", Pass},
		{"go: command not understood", Warn},
	}
	for _, c := range cases {
		status, _, _ := atLeast(c.banner, "1.26", "fix")
		if status != c.want {
			t.Errorf("atLeast(%q) = %s, want %s", c.banner, status, c.want)
		}
	}
}

package checks

import (
	"context"
	"fmt"
	"os/exec"
	"strings"

	"github.com/SekiroKenjii/kakehashi/tools/cli/internal/semver"
)

// The minimum versions, which are the ones the template's own go.mod and global.json ask for.
const (
	minimumGo     = "1.26"
	minimumDotnet = "10.0"
)

func goToolchain() Check {
	return Check{
		Name:  "Go " + minimumGo + "+",
		Level: Required,
		probe: func(ctx context.Context) (Status, string, string) {
			const fix = "install Go: https://go.dev/dl/"
			out, err := output(ctx, "go", "version")
			if err != nil {
				return Fail, "not installed", fix
			}

			status, detail, _ := atLeast(out, minimumGo, fix)
			if status != Fail {
				return status, detail, ""
			}
			// Go fetches the toolchain a module asks for unless it has been told not to, so an
			// older go on PATH still builds the project — once, slowly, on the first build.
			if selection, err := output(ctx, "go", "env", "GOTOOLCHAIN"); err == nil && selection != "local" {
				return Warn, detail + ", GOTOOLCHAIN downloads it", ""
			}
			return Fail, detail + ", and GOTOOLCHAIN=local forbids downloading it", fix
		},
	}
}

func dotnetSDK() Check {
	return Check{
		Name:  ".NET SDK " + minimumDotnet + "+",
		Level: Required,
		probe: func(ctx context.Context) (Status, string, string) {
			const fix = "install the .NET SDK: https://dotnet.microsoft.com/download"
			out, err := output(ctx, "dotnet", "--list-sdks")
			if err != nil {
				return Fail, "not installed", fix
			}

			// One SDK per line, "10.0.100 [C:\Program Files\dotnet\sdk]". Any line that is new
			// enough answers for all of them.
			minimum := semver.MustParse(minimumDotnet)
			newest := ""
			for _, line := range strings.Split(out, "\n") {
				version, err := semver.Parse(strings.TrimSpace(line))
				if err != nil {
					continue
				}
				if version.AtLeast(minimum) {
					return Pass, version.String(), ""
				}
				newest = version.String()
			}
			if newest == "" {
				return Fail, "no SDK installed", fix
			}
			return Fail, newest + ", need " + minimumDotnet, fix
		},
	}
}

func bufCLI() Check {
	return Check{
		Name:     "buf",
		Level:    Required,
		scaffold: true,
		probe: func(ctx context.Context) (Status, string, string) {
			out, err := output(ctx, "buf", "--version")
			if err != nil {
				return Fail, "not installed", "install buf: https://buf.build/docs/installation"
			}
			return Pass, out, ""
		},
	}
}

// protocPlugins is a required check rather than an advisory one because `new` regenerates the
// contract: buf shells out to these two, and a scaffold without them stops before it writes.
func protocPlugins() Check {
	return Check{
		Name:     "protoc-gen-go, protoc-gen-connect-go",
		Level:    Required,
		scaffold: true,
		probe: func(context.Context) (Status, string, string) {
			var missing []string
			for _, plugin := range []string{"protoc-gen-go", "protoc-gen-connect-go"} {
				if _, err := exec.LookPath(plugin); err != nil {
					missing = append(missing, plugin)
				}
			}
			if len(missing) == 0 {
				return Pass, "on PATH", ""
			}
			return Fail, strings.Join(missing, " and ") + " missing",
				"go install google.golang.org/protobuf/cmd/protoc-gen-go@latest && " +
					"go install connectrpc.com/connect/cmd/protoc-gen-connect-go@latest"
		},
	}
}

func gitCLI() Check {
	return Check{
		Name:  "git",
		Level: Advisory,
		probe: func(ctx context.Context) (Status, string, string) {
			out, err := output(ctx, "git", "--version")
			if err != nil {
				return Warn, "not installed", "the scaffold cannot make the project a repository: https://git-scm.com/downloads"
			}
			return Pass, out, ""
		},
	}
}

// atLeast reads a version out of a tool's banner and compares it with the minimum.
func atLeast(banner, minimum, fix string) (Status, string, string) {
	version, err := semver.Parse(banner)
	if err != nil {
		return Warn, banner, ""
	}
	if !version.AtLeast(semver.MustParse(minimum)) {
		return Fail, fmt.Sprintf("%s, need %s", version, minimum), fix
	}
	return Pass, version.String(), ""
}

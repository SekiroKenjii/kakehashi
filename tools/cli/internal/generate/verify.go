package generate

import (
	"context"
	"fmt"
	"os"
	"os/exec"
	"path/filepath"
	"runtime"
	"strings"
	"time"

	"github.com/SekiroKenjii/kakehashi/tools/cli/internal/gen"
)

// toolTimeout bounds one build tool. A restore on a cold machine is minutes; a hung one is forever.
const toolTimeout = 20 * time.Minute

// verifyContract lints the schema and runs the code generator over it. Both are required rather
// than best-effort: the server's wire layer imports what the generator writes, so a module whose
// contract did not generate is a module that cannot compile.
func verifyContract(opts Options, tx *tx, module *gen.Module) error {
	if _, err := exec.LookPath("buf"); err != nil {
		return fmt.Errorf("buf generates the contract this module needs: https://buf.build/docs/installation")
	}

	if out, err := run(opts.Project.Root, "buf", "lint"); err != nil {
		return fmt.Errorf("the generated schema does not lint:\n%s", out)
	}
	opts.Log("linted the schema")

	// Tracked before the generator runs, not after: if it fails half-way there is still a tree to
	// take back.
	for _, path := range module.Generated {
		tx.Track(path)
	}
	if out, err := run(opts.Project.Root, "buf", "generate"); err != nil {
		return fmt.Errorf("buf generate: %w\n%s\n\nprotoc-gen-go and protoc-gen-connect-go have to be on PATH", err, out)
	}
	opts.Log("generated the contract")
	return nil
}

// verifyServer is the server half of the promise: it builds, it vets, and it does not reach across
// a module boundary. archlint is gate 1, and a generated module has to pass it with no exception.
func verifyServer(opts Options) error {
	server := filepath.Join(opts.Project.Root, "server")

	for _, step := range []struct {
		name string
		args []string
	}{
		{"go build", []string{"build", "./..."}},
		{"go vet", []string{"vet", "./..."}},
		{"archlint", []string{"run", "./tools/archlint"}},
	} {
		if out, err := run(server, "go", step.args...); err != nil {
			return fmt.Errorf("the generated server does not pass %s:\n%s", step.name, out)
		}
		opts.Log("%s: green", step.name)
	}
	return nil
}

// verifyClient builds the client and runs its architecture gate, on the operating system that can.
// Elsewhere it says so rather than pretending: a WinUI solution does not build on Linux, and a
// silent skip is what turns "verified" into a word that means nothing.
func verifyClient(opts Options) (verified, skipped []string, err error) {
	solution := solutionIn(filepath.Join(opts.Project.Root, "client"))
	if solution == "" {
		return nil, nil, nil
	}

	if runtime.GOOS != "windows" {
		opts.Log("the client is not verified on %s — the gates for it run on Windows", runtime.GOOS)
		return nil, []string{"dotnet build", "ArchitectureTests"}, nil
	}
	if _, err := exec.LookPath("dotnet"); err != nil {
		opts.Log("the .NET SDK is not installed — the client is written but not verified")
		return nil, []string{"dotnet build", "ArchitectureTests"}, nil
	}

	client := filepath.Join(opts.Project.Root, "client")
	if out, err := run(client, "dotnet", "build", solution, "-p:Platform=x64"); err != nil {
		return nil, nil, fmt.Errorf("the generated client does not build:\n%s", out)
	}
	verified = append(verified, "dotnet build")

	// Gate 2, and only that suite: the rest of the client's tests are not what this changed.
	out, err := run(client, "dotnet", "test", solution, "-p:Platform=x64", "--no-build",
		"--filter", "FullyQualifiedName~ArchitectureTests")
	if err != nil {
		return nil, nil, fmt.Errorf("the generated client does not pass its architecture tests:\n%s", out)
	}
	return append(verified, "ArchitectureTests"), nil, nil
}

// solutionIn names the solution file in a directory, reading it rather than globbing: the project
// root came from the caller, and a bracket in a directory name is a character class to a glob.
func solutionIn(dir string) string {
	entries, err := os.ReadDir(dir)
	if err != nil {
		return ""
	}
	for _, entry := range entries {
		if !entry.IsDir() && strings.HasSuffix(entry.Name(), ".slnx") {
			return entry.Name()
		}
	}
	return ""
}

func run(dir, name string, args ...string) (string, error) {
	ctx, cancel := context.WithTimeout(context.Background(), toolTimeout)
	defer cancel()

	cmd := exec.CommandContext(ctx, name, args...)
	cmd.Dir = dir
	out, err := cmd.CombinedOutput()
	if err != nil {
		return strings.TrimSpace(string(out)), fmt.Errorf("%s %s: %w", name, strings.Join(args, " "), err)
	}
	return strings.TrimSpace(string(out)), nil
}

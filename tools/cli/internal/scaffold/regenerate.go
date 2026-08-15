package scaffold

import (
	"fmt"
	"os"
	"os/exec"
	"path/filepath"
)

// regenerate re-runs the code generator over the substituted schema, and reports whether it ran.
//
// Substituting text into generated protobuf code cannot stand in for this: the descriptor embedded
// in a .pb.go carries byte-length prefixes, so a package name of a different length leaves lengths
// that disagree with the bytes after them, and the server fails to parse its own descriptor at
// startup. That is why this is a hard failure rather than a warning.
func regenerate(work string, log func(string, ...any)) (bool, error) {
	if _, err := os.Stat(filepath.Join(work, "buf.gen.yaml")); err != nil {
		return false, nil
	}
	if _, err := exec.LookPath("buf"); err != nil {
		return false, fmt.Errorf("buf is needed to regenerate the contract: https://buf.build/docs/installation")
	}

	if out, err := run(work, "buf", "generate"); err != nil {
		return false, fmt.Errorf("buf generate: %w\n%s\n\nprotoc-gen-go and protoc-gen-connect-go have to be on PATH", err, out)
	}
	log("regenerated the contract")
	return true, nil
}

// reformat runs the formatter the client's own gate checks with, and reports whether it ran. The
// root namespace sorts somewhere new, so the using blocks it moved through are out of order until
// this runs — which needs the .NET SDK, and on Windows for a WinUI solution. A machine without it
// gets a warning naming the one command to run, rather than a failed scaffold.
func reformat(work string, log func(string, ...any)) bool {
	solutions, err := filepath.Glob(filepath.Join(work, "client", "*.slnx"))
	if err != nil || len(solutions) == 0 {
		return false
	}
	solution := filepath.Base(solutions[0])

	if _, err := exec.LookPath("dotnet"); err != nil {
		log("the .NET SDK is not installed — run 'dotnet format %s' in client/ before committing, "+
			"or the format check will fail on import ordering", solution)
		return false
	}
	if out, err := run(filepath.Join(work, "client"), "dotnet", "format", solution, "--severity", "warn"); err != nil {
		log("could not reformat the client — run 'dotnet format %s' in client/ before committing:\n%s",
			solution, out)
		return false
	}
	log("reformatted the client")
	return true
}

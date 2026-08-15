package scaffold

import (
	"fmt"
	"io/fs"
	"os"
	"os/exec"
	"path/filepath"
	"strings"
)

// regenerate re-runs the code generator over the substituted schema.
//
// Substituting text into generated protobuf code cannot stand in for this: the descriptor embedded
// in a .pb.go carries byte-length prefixes, so a package name of a different length leaves lengths
// that disagree with the bytes after them, and the server fails to parse its own descriptor at
// startup. That is why this is a hard failure rather than a warning.
func regenerate(work string, log func(string, ...any)) error {
	if _, err := os.Stat(filepath.Join(work, "buf.gen.yaml")); err != nil {
		return nil
	}
	if _, err := exec.LookPath("buf"); err != nil {
		return fmt.Errorf("buf is needed to regenerate the contract: https://buf.build/docs/installation")
	}

	if out, err := run(work, "buf", "generate"); err != nil {
		return fmt.Errorf("buf generate: %w\n%s\n\nprotoc-gen-go and protoc-gen-connect-go have to be on PATH", err, out)
	}
	log("regenerated the contract")
	return nil
}

// reformat runs the formatter the client's own gate checks with. The
// root namespace sorts somewhere new, so the using blocks it moved through are out of order until
// this runs — which needs the .NET SDK, and on Windows for a WinUI solution. A machine without it
// gets a warning naming the one command to run, rather than a failed scaffold.
func reformat(work string, log func(string, ...any)) {
	client := filepath.Join(work, "client")
	solution := solutionIn(client)
	if solution == "" {
		return
	}
	if _, err := exec.LookPath("dotnet"); err != nil {
		log("the .NET SDK is not installed — run 'dotnet format %s' in client/ before committing, "+
			"or the format check will fail on import ordering", solution)
		return
	}

	// MSBuild worker nodes outlive the command by fifteen minutes and inherit its directory, and on
	// Windows a directory cannot be renamed while a process is sitting in it. This tree is renamed
	// as soon as the scaffold finishes, so the nodes have to go before it does.
	out, err := runWith(client, []string{"MSBUILDDISABLENODEREUSE=1"}, "dotnet", "format", solution, "--severity", "warn")
	if _, shutdown := run(client, "dotnet", "build-server", "shutdown"); shutdown != nil {
		log("could not stop the .NET build servers: %v", shutdown)
	}
	if err != nil {
		log("could not reformat the client — run 'dotnet format %s' in client/ before committing:\n%s",
			solution, out)
		return
	}
	log("reformatted the client")
}

// solutionIn names the solution file in a directory, or nothing when the tree has no client. It
// reads the directory rather than globbing it: the working directory is built from a path the
// caller chose, and a bracket in that path is a character class to a glob.
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

// buildOutput is what a restore and a compile leave in a tree. XamlCompiler caches namespaces
// under obj/, and a cache pointing at the working directory fails the next build with an error
// naming a path that no longer exists.
var buildOutput = map[string]bool{"obj": true, "bin": true, ".buf-cache": true}

// cleanBuildOutput deletes what the two steps above produced. It runs before the self-check, which
// reads every text file: a restore writes the working directory's absolute path into
// obj/project.assets.json, and that path names the generator.
func cleanBuildOutput(work string) error {
	return filepath.WalkDir(work, func(path string, entry fs.DirEntry, err error) error {
		if err != nil {
			return err
		}
		if !entry.IsDir() || !buildOutput[entry.Name()] {
			return nil
		}
		if err := os.RemoveAll(path); err != nil {
			return err
		}
		return fs.SkipDir
	})
}

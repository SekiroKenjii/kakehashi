package scaffold

import (
	"bytes"
	"io/fs"
	"os"
	"path/filepath"
)

// sniff is how much of a file is read to decide whether it is text. A NUL byte inside it means an
// image or an icon, which no substitution reaches.
const sniff = 8000

// substitute replaces every placeholder in the content of every text file, and reports how many
// files it rewrote.
func substitute(root string, in Inputs) (int, error) {
	count := 0
	err := walkTextFiles(root, func(rel string, body []byte) error {
		if !placeholderPattern.Match(body) {
			return nil
		}

		path := filepath.Join(root, rel)
		info, err := os.Stat(path)
		if err != nil {
			return err
		}
		if err := os.WriteFile(path, []byte(in.apply(string(body))), info.Mode().Perm()); err != nil {
			return err
		}
		count++
		return nil
	})
	return count, err
}

// walkTextFiles reads every regular text file under root and hands the callback its path relative
// to root. Binary files are skipped rather than passed: nothing in this package can act on one.
func walkTextFiles(root string, fn func(rel string, body []byte) error) error {
	return filepath.WalkDir(root, func(path string, entry fs.DirEntry, err error) error {
		if err != nil {
			return err
		}
		if entry.IsDir() {
			if entry.Name() == ".git" {
				return fs.SkipDir
			}
			return nil
		}
		if !entry.Type().IsRegular() {
			return nil
		}

		body, err := os.ReadFile(path)
		if err != nil {
			return err
		}
		if isBinary(body) {
			return nil
		}

		rel, err := filepath.Rel(root, path)
		if err != nil {
			return err
		}
		return fn(rel, body)
	})
}

func isBinary(body []byte) bool {
	if len(body) > sniff {
		body = body[:sniff]
	}
	return bytes.IndexByte(body, 0) >= 0
}

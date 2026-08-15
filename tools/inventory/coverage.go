package main

import (
	"fmt"
	"io"
	"os"
	"path/filepath"
	"strings"
)

// boilerplateMap is the document the coverage check reads.
const boilerplateMap = "docs/BOILERPLATE.md"

// coverage checks the map both ways: every tracked file is classified by some row, and every row
// still covers a tracked file. It reports what fails and returns whether nothing did.
func coverage(root string, files []string, w io.Writer) (bool, error) {
	entries, err := mapEntries(root)
	if err != nil {
		return false, err
	}
	if len(entries) == 0 {
		return false, fmt.Errorf("%s has no map rows", boilerplateMap)
	}

	used := make(map[string]bool, len(entries))
	var uncovered []string

	for _, path := range files {
		matches := covering(entries, path)
		if len(matches) == 0 {
			uncovered = append(uncovered, path)
			continue
		}
		for _, entry := range matches {
			used[entry] = true
		}
	}

	var stale []string
	for _, entry := range entries {
		if !used[entry] {
			stale = append(stale, entry)
		}
	}

	for _, path := range uncovered {
		fmt.Fprintf(w, "unclassified: %s\n", path)
	}
	for _, entry := range stale {
		fmt.Fprintf(w, "covers nothing: %s\n", entry)
	}

	if len(uncovered) == 0 && len(stale) == 0 {
		fmt.Fprintf(w, "%s classifies all %d tracked files\n", boilerplateMap, len(files))
		return true, nil
	}
	return false, nil
}

// mapEntries reads the paths in the first column of the map's table rows. A row whose path ends in
// "/" classifies a directory; anything else classifies exactly one file.
func mapEntries(root string) ([]string, error) {
	body, err := os.ReadFile(filepath.Join(root, filepath.FromSlash(boilerplateMap)))
	if err != nil {
		return nil, err
	}

	var entries []string
	for _, line := range strings.Split(string(body), "\n") {
		line = strings.TrimSpace(line)
		if !strings.HasPrefix(line, "|") {
			continue
		}

		cells := strings.Split(strings.Trim(line, "|"), "|")
		if len(cells) < 2 {
			continue
		}

		path := strings.Trim(strings.TrimSpace(cells[0]), "`")
		// The header row, the separator under it, and the legend's own two-column tables.
		if path == "" || path == "Path" || strings.HasPrefix(path, "-") || strings.HasPrefix(path, ":") {
			continue
		}
		entries = append(entries, path)
	}
	return entries, nil
}

// covering returns every entry that classifies path. A file may be covered more than once — by the
// directory it sits in and by its own row, which is how a hybrid file carries its marker list
// without leaving the directory row uncovered.
func covering(entries []string, path string) []string {
	var matches []string
	for _, entry := range entries {
		if strings.HasSuffix(entry, "/") {
			if strings.HasPrefix(path, entry) {
				matches = append(matches, entry)
			}
			continue
		}
		if path == entry {
			matches = append(matches, entry)
		}
	}
	return matches
}

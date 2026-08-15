package template

import (
	"os"
	"path/filepath"
	"sort"

	"github.com/SekiroKenjii/kakehashi/tools/cli/internal/semver"
)

// DefaultCacheDir is where fetched templates are kept: %LOCALAPPDATA%\kakehashi on Windows,
// ~/.cache/kakehashi elsewhere. A machine with no cache directory falls back to the working
// directory, where a fetch still succeeds and is simply not reused.
func DefaultCacheDir() string {
	base, err := os.UserCacheDir()
	if err != nil {
		return filepath.Join(".kakehashi-cache")
	}
	return filepath.Join(base, "kakehashi")
}

// cached is where one version lives, whether or not it has been fetched. The version reaches here
// normalised, which is what keeps it a single path segment.
func (c *Client) cached(version string) string {
	return filepath.Join(c.CacheDir, "templates", version)
}

// Cached lists the template versions already fetched, oldest first. A directory whose name is not
// exactly a version is not one: an interrupted extraction leaves a staging directory behind, and
// its random suffix is digits, which a laxer reading would take for a version number.
func (c *Client) Cached() ([]string, error) {
	entries, err := os.ReadDir(filepath.Join(c.CacheDir, "templates"))
	if os.IsNotExist(err) {
		return nil, nil
	}
	if err != nil {
		return nil, err
	}

	var versions []semver.Version
	for _, entry := range entries {
		if !entry.IsDir() {
			continue
		}
		v, err := semver.Parse(entry.Name())
		if err != nil || v.String() != entry.Name() {
			continue
		}
		versions = append(versions, v)
	}
	sort.Slice(versions, func(i, j int) bool { return versions[i].Compare(versions[j]) < 0 })

	names := make([]string, 0, len(versions))
	for _, v := range versions {
		names = append(names, v.String())
	}
	return names, nil
}

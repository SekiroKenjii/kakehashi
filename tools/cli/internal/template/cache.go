package template

import (
	"fmt"
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

// cached is where one version lives, whether or not it has been fetched.
func (c *Client) cached(version string) string {
	return filepath.Join(c.CacheDir, "templates", version)
}

// Cached lists the template versions already fetched, oldest first.
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
		if err != nil {
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

func (c *Client) newestCached() (string, error) {
	cached, err := c.Cached()
	if err != nil {
		return "", err
	}
	if len(cached) == 0 {
		return "", fmt.Errorf("no template has been fetched into %s", c.CacheDir)
	}
	return cached[len(cached)-1], nil
}

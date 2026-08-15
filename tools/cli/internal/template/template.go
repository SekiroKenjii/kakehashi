// Package template resolves a template tree to scaffold from: a release fetched over HTTPS,
// verified against the release's checksums and cached, or a checkout named on the command line.
//
// Nothing here needs git. A user's first contact with this tool is the moment they have the least
// installed, and an archive over HTTPS is one dependency fewer than a clone.
package template

import (
	"context"
	"fmt"
	"net/http"
	"os"
	"path/filepath"
	"strings"
	"time"
)

// DefaultSource is the repository the template is released from.
const DefaultSource = "github.com/SekiroKenjii/kakehashi"

// defaultAPI is GitHub's REST endpoint, and a field on Client so a test can serve its own.
const defaultAPI = "https://api.github.com"

// Client resolves template versions against a release channel.
type Client struct {
	Source   string
	API      string
	HTTP     *http.Client
	CacheDir string
	Log      func(format string, args ...any)
}

// Request is one resolution. Dir short-circuits everything else: a directory on disk is already a
// template, and no version resolution, download or cache applies to it.
type Request struct {
	Dir        string
	Version    string
	Offline    bool
	CLIVersion string
}

// Resolved is a template tree ready to scaffold from.
type Resolved struct {
	Dir        string
	Version    string
	Source     string
	Descriptor *Descriptor
}

// New builds a client with the defaults, filling in only what the caller left empty.
func New(c Client) *Client {
	if c.Source == "" {
		c.Source = DefaultSource
	}
	if c.API == "" {
		c.API = defaultAPI
	}
	if c.HTTP == nil {
		c.HTTP = &http.Client{Timeout: 60 * time.Second}
	}
	if c.CacheDir == "" {
		c.CacheDir = DefaultCacheDir()
	}
	if c.Log == nil {
		c.Log = func(string, ...any) {}
	}
	return &c
}

// Resolve produces a template tree, downloading and caching one if it has to.
func (c *Client) Resolve(ctx context.Context, req Request) (*Resolved, error) {
	if req.Dir != "" {
		return c.resolveDirectory(req)
	}

	version, err := c.resolveVersion(ctx, req)
	if err != nil {
		return nil, err
	}

	dir := c.cached(version)
	if _, err := os.Stat(dir); err != nil {
		if req.Offline {
			return nil, fmt.Errorf("template %s is not in the cache and --offline forbids fetching it%s",
				version, c.cacheHint())
		}
		if dir, err = c.fetch(ctx, version); err != nil {
			return nil, err
		}
	}

	descriptor, err := LoadDescriptor(dir, req.CLIVersion)
	if err != nil {
		return nil, err
	}
	return &Resolved{Dir: dir, Version: version, Source: c.Source, Descriptor: descriptor}, nil
}

// resolveDirectory takes a checkout at its word: the version is what the tree says about itself,
// and the source is where it was read from rather than the release channel it never touched.
func (c *Client) resolveDirectory(req Request) (*Resolved, error) {
	dir, err := filepath.Abs(req.Dir)
	if err != nil {
		return nil, err
	}

	descriptor, err := LoadDescriptor(dir, req.CLIVersion)
	if err != nil {
		return nil, err
	}
	return &Resolved{
		Dir:        dir,
		Version:    descriptor.TemplateVersion,
		Source:     filepath.ToSlash(dir),
		Descriptor: descriptor,
	}, nil
}

// resolveVersion turns "the latest one" into a number, from the release channel or, offline, from
// what has already been fetched.
func (c *Client) resolveVersion(ctx context.Context, req Request) (string, error) {
	if req.Version != "" {
		return strings.TrimPrefix(strings.TrimPrefix(req.Version, TagPrefix), "v"), nil
	}
	if req.Offline {
		version, err := c.newestCached()
		if err != nil {
			return "", fmt.Errorf("--offline and no template in the cache: %w", err)
		}
		return version, nil
	}
	return c.latestVersion(ctx)
}

func (c *Client) cacheHint() string {
	cached, err := c.Cached()
	if err != nil || len(cached) == 0 {
		return ""
	}
	return " (cached: " + strings.Join(cached, ", ") + ")"
}

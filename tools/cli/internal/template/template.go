// Package template resolves a template tree to scaffold from: a release fetched over HTTPS,
// verified against the release's checksums and cached, or a checkout named on the command line.
//
// Nothing here shells out to git. Fetching an archive over HTTPS is one prerequisite fewer than a
// clone, and this runs before the user has installed anything.
package template

import (
	"context"
	"errors"
	"fmt"
	"net/http"
	"os"
	"path/filepath"
	"strings"
	"time"

	"github.com/SekiroKenjii/kakehashi/tools/cli/internal/semver"
)

// DefaultSource is the repository the template is released from.
const DefaultSource = "github.com/SekiroKenjii/kakehashi"

// defaultAPI is GitHub's REST endpoint, and a field on Client so a test can serve its own.
const defaultAPI = "https://api.github.com"

// transferTimeout bounds one HTTP exchange including its body, which for the archive is megabytes.
const transferTimeout = 10 * time.Minute

// candidates bounds how far back Resolve walks looking for a release this CLI can use. Past this
// the answer is that the two versions are too far apart to bridge, and saying so beats fetching.
const candidates = 5

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
		c.HTTP = &http.Client{Timeout: transferTimeout}
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
//
// With no version asked for, it takes the newest release this CLI is compatible with rather than
// the newest release: a template states the CLI range it needs, and walking back to the newest one
// in range is what lets an older CLI keep working after a template raises its floor.
func (c *Client) Resolve(ctx context.Context, req Request) (*Resolved, error) {
	if req.Dir != "" {
		return c.resolveDirectory(req)
	}
	if req.Version != "" {
		version, err := normalise(req.Version)
		if err != nil {
			return nil, fmt.Errorf("--template-version %q: %w", req.Version, err)
		}
		return c.resolveVersion(ctx, version, req)
	}

	wanted, err := c.published(ctx, req)
	if err != nil {
		return nil, err
	}

	var refused error
	for i, version := range wanted {
		if i == candidates {
			break
		}
		resolved, err := c.resolveVersion(ctx, version, req)
		if err == nil {
			return resolved, nil
		}
		if !errors.Is(err, ErrIncompatible) {
			return nil, err
		}
		refused = err
		c.Log("%v — looking for an older template", err)
	}
	return nil, refused
}

// published lists the versions to consider, newest first: what the channel has, or what has already
// been fetched when the caller is offline.
func (c *Client) published(ctx context.Context, req Request) ([]string, error) {
	if req.Offline {
		cached, err := c.Cached()
		if err != nil {
			return nil, err
		}
		if len(cached) == 0 {
			return nil, fmt.Errorf("--offline and no template has been fetched into %s", c.CacheDir)
		}
		return reversed(cached), nil
	}

	versions, err := c.versions(ctx)
	if err != nil {
		return nil, err
	}
	names := make([]string, 0, len(versions))
	for _, v := range versions {
		names = append(names, v.String())
	}
	return names, nil
}

// resolveVersion produces one version, from the cache or from the channel.
func (c *Client) resolveVersion(ctx context.Context, version string, req Request) (*Resolved, error) {
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

// normalise turns every spelling of a version into the major.minor.patch the cache is keyed by.
// The result is a path segment, so it has to be a version and nothing else.
func normalise(version string) (string, error) {
	v, err := semver.Parse(strings.TrimPrefix(strings.TrimPrefix(version, TagPrefix), "v"))
	if err != nil {
		return "", err
	}
	return v.String(), nil
}

func reversed(versions []string) []string {
	out := make([]string, 0, len(versions))
	for i := len(versions) - 1; i >= 0; i-- {
		out = append(out, versions[i])
	}
	return out
}

func (c *Client) cacheHint() string {
	cached, err := c.Cached()
	if err != nil || len(cached) == 0 {
		return ""
	}
	return " (cached: " + strings.Join(cached, ", ") + ")"
}

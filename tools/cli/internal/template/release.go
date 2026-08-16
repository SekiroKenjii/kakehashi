package template

import (
	"context"
	"encoding/json"
	"fmt"
	"io"
	"net/http"
	"net/url"
	"os"
	"sort"
	"strings"

	"github.com/SekiroKenjii/kakehashi/tools/cli/internal/semver"
)

// TagPrefix is what a template release is tagged with. The CLI releases from the same repository
// under cli/, so both series share one release list and the prefix is part of every query.
const TagPrefix = "template/v"

// How much of the release list to read. The two series interleave by date, so a page of 100 is not
// a page of 100 template releases; pages are followed until one comes back short.
const (
	releasesPerPage = 100
	releasePages    = 5
	releaseListMax  = 8 << 20
)

// release is the part of the GitHub release representation this tool reads.
type release struct {
	Tag        string `json:"tag_name"`
	Draft      bool   `json:"draft"`
	Prerelease bool   `json:"prerelease"`
	Assets     []struct {
		Name string `json:"name"`
		URL  string `json:"browser_download_url"`
	} `json:"assets"`

	version semver.Version
}

// versions lists the published template versions, newest first. The API returns releases by
// creation date, which is not version order: a patch to an old line is published last.
//
// A draft is visible only to a caller with push access, and a prerelease is not what "the newest"
// means to anyone else, so both are skipped — otherwise the version a command resolves depends on
// who is running it.
func (c *Client) versions(ctx context.Context) ([]semver.Version, error) {
	releases, err := c.releases(ctx)
	if err != nil {
		return nil, err
	}

	var published []semver.Version
	for _, r := range releases {
		if r.Draft || r.Prerelease || !strings.HasPrefix(r.Tag, TagPrefix) {
			continue
		}
		if v, err := semver.Parse(strings.TrimPrefix(r.Tag, TagPrefix)); err == nil {
			published = append(published, v)
		}
	}
	if len(published) == 0 {
		return nil, fmt.Errorf("%s has published no %s release", c.Source, TagPrefix)
	}

	sort.Slice(published, func(i, j int) bool { return published[i].Compare(published[j]) > 0 })
	return published, nil
}

// release finds one release by version. It compares parsed versions rather than tag text: a tag
// spelled template/v0.3 is the same release as one spelled template/v0.3.0, and the caller only
// ever holds the parsed form.
func (c *Client) release(ctx context.Context, version string) (*release, error) {
	releases, err := c.releases(ctx)
	if err != nil {
		return nil, err
	}

	want, err := semver.Parse(version)
	if err != nil {
		return nil, err
	}
	for _, r := range releases {
		if r.Draft || !strings.HasPrefix(r.Tag, TagPrefix) {
			continue
		}
		if v, err := semver.Parse(strings.TrimPrefix(r.Tag, TagPrefix)); err == nil && v.Compare(want) == 0 {
			r.version = v
			return &r, nil
		}
	}
	return nil, fmt.Errorf("%s has no release tagged %s%s", c.Source, TagPrefix, version)
}

func (c *Client) releases(ctx context.Context) ([]release, error) {
	owner, repo, err := c.repository()
	if err != nil {
		return nil, err
	}

	var all []release
	for page := 1; page <= releasePages; page++ {
		address := fmt.Sprintf("%s/repos/%s/%s/releases?per_page=%d&page=%d",
			c.API, owner, repo, releasesPerPage, page)
		body, err := c.get(ctx, address)
		if err != nil {
			return nil, err
		}

		var batch []release
		err = json.NewDecoder(io.LimitReader(body, releaseListMax)).Decode(&batch)
		body.Close()
		if err != nil {
			return nil, fmt.Errorf("read the release list: %w", err)
		}

		all = append(all, batch...)
		if len(batch) < releasesPerPage {
			break
		}
	}
	return all, nil
}

// get performs a request, carrying a token when the environment has one and the address is one the
// token belongs to: an unauthenticated runner shares a rate limit with everyone else on its
// address, and a credential belongs only to the host the caller named.
func (c *Client) get(ctx context.Context, address string) (io.ReadCloser, error) {
	request, err := http.NewRequestWithContext(ctx, http.MethodGet, address, nil)
	if err != nil {
		return nil, err
	}
	request.Header.Set("Accept", "application/vnd.github+json")
	if token := githubToken(); token != "" && c.trusted(address) {
		request.Header.Set("Authorization", "Bearer "+token)
	}

	response, err := c.HTTP.Do(request)
	if err != nil {
		return nil, fmt.Errorf("reach %s: %w", address, err)
	}
	if response.StatusCode != http.StatusOK {
		response.Body.Close()
		return nil, fmt.Errorf("%s: %s", address, response.Status)
	}
	return response.Body, nil
}

// trusted reports whether an address may see the token. The asset URLs come out of the release
// JSON, so without this the response would choose where the credential goes.
func (c *Client) trusted(address string) bool {
	target, err := url.Parse(address)
	if err != nil {
		return false
	}
	api, err := url.Parse(c.API)
	if err != nil {
		return false
	}
	if target.Host == api.Host {
		return true
	}
	return target.Scheme == "https" &&
		(target.Host == "github.com" || strings.HasSuffix(target.Host, ".github.com"))
}

// repository splits the source into the owner and repository the API addresses.
func (c *Client) repository() (owner, repo string, err error) {
	parts := strings.Split(strings.TrimSuffix(c.Source, "/"), "/")
	if len(parts) < 3 {
		return "", "", fmt.Errorf("template source %q is not host/owner/repository", c.Source)
	}
	return parts[len(parts)-2], parts[len(parts)-1], nil
}

func githubToken() string {
	for _, name := range []string{"GITHUB_TOKEN", "GH_TOKEN"} {
		if token := os.Getenv(name); token != "" {
			return token
		}
	}
	return ""
}

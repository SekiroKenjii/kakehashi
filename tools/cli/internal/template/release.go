package template

import (
	"context"
	"encoding/json"
	"fmt"
	"io"
	"net/http"
	"os"
	"strings"

	"github.com/SekiroKenjii/kakehashi/tools/cli/internal/semver"
)

// TagPrefix is what a template release is tagged with. The CLI releases from the same repository
// under cli/, which is why the prefix is part of the query rather than assumed.
const TagPrefix = "template/v"

// releases is the largest page the API serves. A template that outgrows one page of releases has
// hundreds of versions, and the newest is on the first page in any case.
const releasesPerPage = 100

// release is the part of the GitHub release representation this tool reads.
type release struct {
	Tag    string `json:"tag_name"`
	Assets []struct {
		Name string `json:"name"`
		URL  string `json:"browser_download_url"`
	} `json:"assets"`
}

// latestVersion is the newest template release by version order, which is not the order the API
// returns them in: releases come back by creation date, and a patch to an old line is newer.
func (c *Client) latestVersion(ctx context.Context) (string, error) {
	releases, err := c.releases(ctx)
	if err != nil {
		return "", err
	}

	best, found := semver.Version{}, ""
	for _, r := range releases {
		if !strings.HasPrefix(r.Tag, TagPrefix) {
			continue
		}
		v, err := semver.Parse(strings.TrimPrefix(r.Tag, TagPrefix))
		if err != nil {
			continue
		}
		if found == "" || v.Compare(best) > 0 {
			best, found = v, v.String()
		}
	}
	if found == "" {
		return "", fmt.Errorf("%s has published no %s release", c.Source, TagPrefix)
	}
	return found, nil
}

// release finds one release by version. Listing and matching, rather than asking for the tag by
// name: a tag with a slash in it has to be escaped into the path, and proxies disagree about how.
func (c *Client) release(ctx context.Context, version string) (*release, error) {
	releases, err := c.releases(ctx)
	if err != nil {
		return nil, err
	}

	tag := TagPrefix + version
	for _, r := range releases {
		if r.Tag == tag {
			return &r, nil
		}
	}
	return nil, fmt.Errorf("%s has no release tagged %s", c.Source, tag)
}

func (c *Client) releases(ctx context.Context) ([]release, error) {
	owner, repo, err := c.repository()
	if err != nil {
		return nil, err
	}

	url := fmt.Sprintf("%s/repos/%s/%s/releases?per_page=%d", c.API, owner, repo, releasesPerPage)
	body, err := c.get(ctx, url)
	if err != nil {
		return nil, err
	}
	defer body.Close()

	var releases []release
	if err := json.NewDecoder(body).Decode(&releases); err != nil {
		return nil, fmt.Errorf("read the release list: %w", err)
	}
	return releases, nil
}

// get performs a request, carrying a token when the environment has one: an unauthenticated
// runner shares a rate limit with everyone else on its address.
func (c *Client) get(ctx context.Context, url string) (io.ReadCloser, error) {
	request, err := http.NewRequestWithContext(ctx, http.MethodGet, url, nil)
	if err != nil {
		return nil, err
	}
	request.Header.Set("Accept", "application/vnd.github+json")
	if token := githubToken(); token != "" {
		request.Header.Set("Authorization", "Bearer "+token)
	}

	response, err := c.HTTP.Do(request)
	if err != nil {
		return nil, fmt.Errorf("reach %s: %w", url, err)
	}
	if response.StatusCode != http.StatusOK {
		response.Body.Close()
		return nil, fmt.Errorf("%s: %s", url, response.Status)
	}
	return response.Body, nil
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

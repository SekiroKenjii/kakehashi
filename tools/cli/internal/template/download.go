package template

import (
	"context"
	"crypto/sha256"
	"encoding/hex"
	"fmt"
	"io"
	"os"
	"strings"
)

// checksumsAsset is the release's own list of digests. The archive beside it is built by CI rather
// than taken from GitHub's generated source tarball: a generated archive is repacked on demand and
// its checksum is not stable, and a checksum that changes under you is worth nothing.
const checksumsAsset = "checksums.txt"

// fetch downloads a template release, verifies it against the release's own checksums file, and
// leaves it extracted in the cache. The returned directory is the cached tree.
func (c *Client) fetch(ctx context.Context, version string) (string, error) {
	release, err := c.release(ctx, version)
	if err != nil {
		return "", err
	}

	// From the tag rather than from the normalised version: the asset is named after the tag that
	// built it, and template/v0.3 ships template-v0.3.tar.gz.
	name := "template-" + strings.TrimPrefix(release.Tag, "template/") + ".tar.gz"
	archiveURL, err := asset(release, name)
	if err != nil {
		return "", err
	}
	checksumsURL, err := asset(release, checksumsAsset)
	if err != nil {
		return "", err
	}

	want, err := c.checksum(ctx, checksumsURL, name)
	if err != nil {
		return "", err
	}

	c.Log("fetching template v%s", version)
	archive, sum, err := c.downloadToTemp(ctx, archiveURL)
	if archive != "" {
		defer os.Remove(archive)
	}
	if err != nil {
		return "", err
	}
	if sum != want {
		return "", fmt.Errorf("%s does not match the checksum in %s:\n  got  %s\n  want %s",
			name, checksumsAsset, sum, want)
	}

	target := c.cached(version)
	if err := extract(archive, target); err != nil {
		return "", err
	}
	return target, nil
}

// checksum reads the sha256sum-formatted list and returns the digest recorded for one file.
func (c *Client) checksum(ctx context.Context, url, name string) (string, error) {
	body, err := c.get(ctx, url)
	if err != nil {
		return "", err
	}
	defer body.Close()

	list, err := io.ReadAll(io.LimitReader(body, 1<<20))
	if err != nil {
		return "", err
	}
	for _, line := range strings.Split(string(list), "\n") {
		fields := strings.Fields(line)
		if len(fields) == 2 && strings.TrimPrefix(fields[1], "*") == name {
			return strings.ToLower(fields[0]), nil
		}
	}
	return "", fmt.Errorf("%s lists no checksum for %s", checksumsAsset, name)
}

// downloadToTemp streams the asset to a temporary file, hashing it on the way through so the
// archive is never read twice and never held in memory.
func (c *Client) downloadToTemp(ctx context.Context, url string) (path, sum string, err error) {
	body, err := c.get(ctx, url)
	if err != nil {
		return "", "", err
	}
	defer body.Close()

	file, err := os.CreateTemp("", "kakehashi-template-*.tar.gz")
	if err != nil {
		return "", "", err
	}
	defer file.Close()

	digest := sha256.New()
	if _, err := io.Copy(io.MultiWriter(file, digest), io.LimitReader(body, maxArchiveBytes)); err != nil {
		return file.Name(), "", err
	}
	return file.Name(), hex.EncodeToString(digest.Sum(nil)), nil
}

func asset(r *release, name string) (string, error) {
	for _, a := range r.Assets {
		if a.Name == name {
			return a.URL, nil
		}
	}
	return "", fmt.Errorf("release %s carries no asset named %s", r.Tag, name)
}

package template

import (
	"context"
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"errors"
	"fmt"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"strings"
	"sync/atomic"
	"testing"
)

const descriptor = `{
  "schemaVersion": 1,
  "templateVersion": "%s",
  "requiresCli": "%s",
  "markersSchema": 1,
  "unitsSchema": 1,
  "exampleUnits": ["example"]
}
`

// stub is one release the fake channel serves.
type stub struct {
	version     string
	requiresCLI string
	draft       bool
	prerelease  bool
}

// channel is a stand-in for the release channel: it serves a release list, the archive assets and
// the checksums file, and counts what was asked for.
type channel struct {
	*httptest.Server
	requests atomic.Int64
	archives map[string][]byte
	corrupt  bool
}

func newChannel(t *testing.T, versions ...string) *channel {
	t.Helper()
	releases := make([]stub, 0, len(versions))
	for _, version := range versions {
		releases = append(releases, stub{version: version, requiresCLI: ">=1.1.0"})
	}
	return newChannelOf(t, releases...)
}

func newChannelOf(t *testing.T, releases ...stub) *channel {
	t.Helper()
	c := &channel{archives: map[string][]byte{}}
	for _, r := range releases {
		c.archives[r.version] = tarball(t, []entry{
			{name: DescriptorName, body: fmt.Sprintf(descriptor, r.version, r.requiresCLI)},
			{name: "README.md", body: "# " + r.version + "\n"},
		})
	}

	mux := http.NewServeMux()
	mux.HandleFunc("/repos/owner/repo/releases", func(w http.ResponseWriter, r *http.Request) {
		c.requests.Add(1)
		base := "http://" + r.Host

		// The first page carries the CLI series as well; the second is always empty, which is how
		// the client learns to stop.
		body := []map[string]any{}
		if r.URL.Query().Get("page") == "1" {
			body = append(body, map[string]any{"tag_name": "tools/cli/v9.9.9"})
			for _, release := range releases {
				body = append(body, map[string]any{
					"tag_name":   TagPrefix + release.version,
					"draft":      release.draft,
					"prerelease": release.prerelease,
					"assets": []map[string]string{
						{
							"name":                 "template-v" + release.version + ".tar.gz",
							"browser_download_url": base + "/download/" + release.version + "/archive",
						},
						{
							"name":                 checksumsAsset,
							"browser_download_url": base + "/download/" + release.version + "/checksums",
						},
					},
				})
			}
		}
		if err := json.NewEncoder(w).Encode(body); err != nil {
			t.Error(err)
		}
	})
	mux.HandleFunc("/download/", func(w http.ResponseWriter, r *http.Request) {
		c.requests.Add(1)
		parts := strings.Split(strings.TrimPrefix(r.URL.Path, "/download/"), "/")
		archive := c.archives[parts[0]]

		if parts[1] == "checksums" {
			body := archive
			if c.corrupt {
				body = append(append([]byte{}, archive...), 'x')
			}
			sum := sha256.Sum256(body)
			fmt.Fprintf(w, "%s  template-v%s.tar.gz\n", hex.EncodeToString(sum[:]), parts[0])
			return
		}
		if _, err := w.Write(archive); err != nil {
			t.Error(err)
		}
	})

	c.Server = httptest.NewServer(mux)
	t.Cleanup(c.Close)
	return c
}

func client(t *testing.T, c *channel) *Client {
	t.Helper()
	return New(Client{
		Source:   "github.com/owner/repo",
		API:      c.URL,
		CacheDir: filepath.Join(t.TempDir(), "cache"),
	})
}

func TestResolveFetchesVerifiesAndCaches(t *testing.T) {
	channel := newChannel(t, "1.1.0", "1.2.0")
	client := client(t, channel)

	resolved, err := client.Resolve(context.Background(), Request{CLIVersion: "1.1.0"})
	if err != nil {
		t.Fatalf("Resolve: %v", err)
	}
	if resolved.Version != "1.2.0" {
		t.Errorf("version = %s, want 1.2.0", resolved.Version)
	}
	if resolved.Descriptor.TemplateVersion != "1.2.0" {
		t.Errorf("descriptor = %+v", resolved.Descriptor)
	}
	if !strings.HasPrefix(resolved.Dir, client.CacheDir) {
		t.Errorf("dir = %s, want it under %s", resolved.Dir, client.CacheDir)
	}
	if _, err := os.Stat(filepath.Join(resolved.Dir, "README.md")); err != nil {
		t.Errorf("the archive was not extracted: %v", err)
	}

	// A second run is the cache's whole purpose.
	served := channel.requests.Load()
	if _, err := client.Resolve(context.Background(), Request{CLIVersion: "1.1.0"}); err != nil {
		t.Fatalf("Resolve from cache: %v", err)
	}
	if fetched := channel.requests.Load() - served; fetched > 1 {
		t.Errorf("the second resolve made %d requests, want only the version lookup", fetched)
	}
}

// The API returns releases newest-first by date, and a patch to an old line is published last.
func TestResolvePicksTheHighestVersionNotTheLatestRelease(t *testing.T) {
	client := client(t, newChannel(t, "1.9.0", "1.10.0", "1.9.1"))

	resolved, err := client.Resolve(context.Background(), Request{CLIVersion: "1.1.0"})
	if err != nil {
		t.Fatalf("Resolve: %v", err)
	}
	if resolved.Version != "1.10.0" {
		t.Errorf("version = %s, want 1.10.0", resolved.Version)
	}
}

func TestResolveAcceptsEverySpellingOfAPinnedVersion(t *testing.T) {
	client := client(t, newChannel(t, "1.1.0", "1.2.0"))

	for _, pinned := range []string{"1.1.0", "v1.1.0", "template/v1.1.0"} {
		resolved, err := client.Resolve(context.Background(), Request{Version: pinned, CLIVersion: "1.1.0"})
		if err != nil {
			t.Fatalf("Resolve(%s): %v", pinned, err)
		}
		if resolved.Version != "1.1.0" {
			t.Errorf("Resolve(%s) = %s, want 1.1.0", pinned, resolved.Version)
		}
	}
}

// §2 step 2: the default is the newest template this CLI is compatible with, not the newest
// template. Otherwise a template that raises its floor strands every CLI below it.
func TestResolveFallsBackToTheNewestCompatibleRelease(t *testing.T) {
	client := client(t, newChannelOf(t,
		stub{version: "1.1.0", requiresCLI: ">=1.1.0 <1.2.0"},
		stub{version: "1.2.0", requiresCLI: ">=1.2.0 <1.3.0"},
		stub{version: "1.3.0", requiresCLI: ">=1.3.0"},
	))

	resolved, err := client.Resolve(context.Background(), Request{CLIVersion: "1.1.5"})
	if err != nil {
		t.Fatalf("Resolve: %v", err)
	}
	if resolved.Version != "1.1.0" {
		t.Errorf("version = %s, want the newest one this CLI can use", resolved.Version)
	}
}

// With nothing in range the refusal has to name a version pair, so the reader knows which of the
// two to move.
func TestResolveReportsWhenNoReleaseIsCompatible(t *testing.T) {
	client := client(t, newChannelOf(t, stub{version: "1.3.0", requiresCLI: ">=1.3.0"}))

	_, err := client.Resolve(context.Background(), Request{CLIVersion: "1.1.0"})
	if err == nil {
		t.Fatal("Resolve accepted a template outside this CLI's range")
	}
	if !errors.Is(err, ErrIncompatible) {
		t.Errorf("error is not an incompatibility: %v", err)
	}
	if !strings.Contains(err.Error(), "1.3.0") || !strings.Contains(err.Error(), "1.1.0") {
		t.Errorf("error %q does not name both versions", err)
	}
}

// A draft is visible only to a caller with a token, and a prerelease is not what "newest" means.
// Either one would make the resolved version depend on who is running the command.
func TestResolveSkipsDraftsAndPrereleases(t *testing.T) {
	client := client(t, newChannelOf(t,
		stub{version: "1.1.0", requiresCLI: ">=1.1.0"},
		stub{version: "1.2.0", requiresCLI: ">=1.1.0", draft: true},
		stub{version: "1.3.0", requiresCLI: ">=1.1.0", prerelease: true},
	))

	resolved, err := client.Resolve(context.Background(), Request{CLIVersion: "1.1.0"})
	if err != nil {
		t.Fatalf("Resolve: %v", err)
	}
	if resolved.Version != "1.1.0" {
		t.Errorf("version = %s, want the newest published release", resolved.Version)
	}
}

// The version becomes a cache path segment.
func TestResolveRefusesAPinnedVersionThatIsNotAVersion(t *testing.T) {
	client := client(t, newChannel(t, "1.1.0"))

	for _, pinned := range []string{"../../../../tmp/evil", "v", "latest"} {
		if _, err := client.Resolve(context.Background(), Request{Version: pinned, CLIVersion: "1.1.0"}); err == nil {
			t.Errorf("Resolve accepted --template-version %q", pinned)
		}
	}
}

// MkdirTemp's suffix is digits, and a killed process leaves the staging directory behind.
func TestCachedIgnoresAnInterruptedExtraction(t *testing.T) {
	client := client(t, newChannel(t, "1.1.0"))
	templates := filepath.Join(client.CacheDir, "templates")
	for _, name := range []string{"1.1.0", ".extract-3703434504", "notes"} {
		if err := os.MkdirAll(filepath.Join(templates, name), 0o755); err != nil {
			t.Fatal(err)
		}
	}

	cached, err := client.Cached()
	if err != nil {
		t.Fatalf("Cached: %v", err)
	}
	if len(cached) != 1 || cached[0] != "1.1.0" {
		t.Errorf("Cached = %v, want only the one version", cached)
	}
}

func TestResolveRefusesAnArchiveThatDoesNotMatchItsChecksum(t *testing.T) {
	channel := newChannel(t, "1.1.0")
	channel.corrupt = true
	client := client(t, channel)

	_, err := client.Resolve(context.Background(), Request{CLIVersion: "1.1.0"})
	if err == nil {
		t.Fatal("Resolve accepted an archive that does not match its checksum")
	}
	if !strings.Contains(err.Error(), "checksum") {
		t.Errorf("error %q does not say what went wrong", err)
	}

	cached, err := client.Cached()
	if err != nil {
		t.Fatal(err)
	}
	if len(cached) > 0 {
		t.Errorf("a refused archive was cached: %v", cached)
	}
}

func TestResolveOffline(t *testing.T) {
	channel := newChannel(t, "1.1.0")
	client := client(t, channel)

	if _, err := client.Resolve(context.Background(), Request{Offline: true, CLIVersion: "1.1.0"}); err == nil {
		t.Fatal("Resolve --offline succeeded with an empty cache")
	}

	if _, err := client.Resolve(context.Background(), Request{CLIVersion: "1.1.0"}); err != nil {
		t.Fatalf("Resolve: %v", err)
	}
	channel.Close()

	resolved, err := client.Resolve(context.Background(), Request{Offline: true, CLIVersion: "1.1.0"})
	if err != nil {
		t.Fatalf("Resolve --offline from the cache: %v", err)
	}
	if resolved.Version != "1.1.0" {
		t.Errorf("version = %s, want 1.1.0", resolved.Version)
	}
}

func TestResolveReportsAVersionThatWasNeverReleased(t *testing.T) {
	client := client(t, newChannel(t, "1.1.0"))

	_, err := client.Resolve(context.Background(), Request{Version: "7.7.7", CLIVersion: "1.1.0"})
	if err == nil {
		t.Fatal("Resolve found a version that was never released")
	}
	if !strings.Contains(err.Error(), "template/v7.7.7") {
		t.Errorf("error %q does not name the tag it looked for", err)
	}
}

func TestResolveDirectorySkipsTheReleaseChannel(t *testing.T) {
	dir := t.TempDir()
	if err := os.MkdirAll(filepath.Join(dir, "templates"), 0o755); err != nil {
		t.Fatal(err)
	}
	body := fmt.Sprintf(descriptor, "1.4.5", ">=1.1.0")
	if err := os.WriteFile(filepath.Join(dir, filepath.FromSlash(DescriptorName)), []byte(body), 0o644); err != nil {
		t.Fatal(err)
	}

	// No channel at all: a directory resolves without one.
	client := New(Client{Source: "github.com/owner/repo", API: "http://127.0.0.1:0", CacheDir: t.TempDir()})

	resolved, err := client.Resolve(context.Background(), Request{Dir: dir, CLIVersion: "1.1.0"})
	if err != nil {
		t.Fatalf("Resolve: %v", err)
	}
	if resolved.Version != "1.4.5" {
		t.Errorf("version = %s, want the one the checkout declares", resolved.Version)
	}
	if resolved.Dir != dir {
		t.Errorf("dir = %s, want %s", resolved.Dir, dir)
	}
	if !strings.HasSuffix(resolved.Source, filepath.ToSlash(dir)) {
		t.Errorf("source = %s, want the directory it was read from", resolved.Source)
	}
}

package template

import (
	"archive/tar"
	"bytes"
	"compress/gzip"
	"os"
	"path/filepath"
	"strings"
	"testing"
)

type entry struct {
	name string
	body string
	mode int64
	kind byte
	link string
}

func tarball(t *testing.T, entries []entry) []byte {
	t.Helper()
	buffer := &bytes.Buffer{}
	zipped := gzip.NewWriter(buffer)
	writer := tar.NewWriter(zipped)

	for _, e := range entries {
		kind := e.kind
		if kind == 0 {
			kind = tar.TypeReg
		}
		mode := e.mode
		if mode == 0 {
			mode = 0o644
		}
		header := &tar.Header{
			Name:     e.name,
			Mode:     mode,
			Size:     int64(len(e.body)),
			Typeflag: kind,
			Linkname: e.link,
		}
		if kind == tar.TypeDir {
			header.Size = 0
		}
		if err := writer.WriteHeader(header); err != nil {
			t.Fatal(err)
		}
		if kind == tar.TypeReg {
			if _, err := writer.Write([]byte(e.body)); err != nil {
				t.Fatal(err)
			}
		}
	}
	if err := writer.Close(); err != nil {
		t.Fatal(err)
	}
	if err := zipped.Close(); err != nil {
		t.Fatal(err)
	}
	return buffer.Bytes()
}

func write(t *testing.T, body []byte) string {
	t.Helper()
	path := filepath.Join(t.TempDir(), "archive.tar.gz")
	if err := os.WriteFile(path, body, 0o644); err != nil {
		t.Fatal(err)
	}
	return path
}

func TestExtract(t *testing.T) {
	archive := write(t, tarball(t, []entry{
		{name: "README.md", body: "hello\n"},
		{name: "tools/build.sh", body: "#!/bin/sh\n", mode: 0o755},
	}))
	target := filepath.Join(t.TempDir(), "templates", "1.0.0")

	if err := extract(archive, target); err != nil {
		t.Fatalf("extract: %v", err)
	}

	body, err := os.ReadFile(filepath.Join(target, "README.md"))
	if err != nil || string(body) != "hello\n" {
		t.Errorf("README.md = %q, %v", body, err)
	}
	info, err := os.Stat(filepath.Join(target, "tools", "build.sh"))
	if err != nil {
		t.Fatal(err)
	}
	if info.Mode().Perm()&0o111 == 0 {
		t.Errorf("build.sh lost its executable bit: %v", info.Mode())
	}
}

// `tar czf x.tar.gz repo/` wraps everything in one directory; a release built from inside the tree
// does not. Both have to scaffold.
func TestExtractStripsASingleRootDirectory(t *testing.T) {
	archive := write(t, tarball(t, []entry{
		{name: "kakehashi-1.0.0/", kind: tar.TypeDir},
		{name: "kakehashi-1.0.0/README.md", body: "hello\n"},
	}))
	target := filepath.Join(t.TempDir(), "extracted")

	if err := extract(archive, target); err != nil {
		t.Fatalf("extract: %v", err)
	}
	if _, err := os.Stat(filepath.Join(target, "README.md")); err != nil {
		t.Errorf("the wrapping directory was not stripped: %v", err)
	}
}

func TestExtractRefusals(t *testing.T) {
	cases := []struct {
		name    string
		entries []entry
		says    string
	}{
		{"a path that climbs out", []entry{{name: "../escaped.txt", body: "x"}}, "escapes"},
		{"an absolute path", []entry{{name: "/etc/passwd", body: "x"}}, "escapes"},
		{"a symlink", []entry{{name: "link", kind: tar.TypeSymlink, link: "/etc/passwd"}}, "unsupported"},
		{"an empty archive", nil, "empty"},
	}
	for _, c := range cases {
		t.Run(c.name, func(t *testing.T) {
			target := filepath.Join(t.TempDir(), "extracted")

			err := extract(write(t, tarball(t, c.entries)), target)
			if err == nil {
				t.Fatalf("extract accepted %s", c.name)
			}
			if !strings.Contains(err.Error(), c.says) {
				t.Errorf("error %q does not mention %q", err, c.says)
			}
			if _, err := os.Stat(target); !os.IsNotExist(err) {
				t.Errorf("%s was created from an archive that was refused", target)
			}
		})
	}
}

func TestExtractRefusesSomethingThatIsNotAnArchive(t *testing.T) {
	if err := extract(write(t, []byte("not gzipped")), filepath.Join(t.TempDir(), "x")); err == nil {
		t.Error("extract accepted a file that is not a gzip archive")
	}
}

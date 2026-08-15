package template

import (
	"archive/tar"
	"compress/gzip"
	"fmt"
	"io"
	"os"
	"path/filepath"
	"strings"
)

// maxArchiveBytes bounds both the download and what it expands to. A template is a few megabytes;
// anything past this is a mistake or an attack, and either way the disk is not the place to find
// out.
const maxArchiveBytes = 512 << 20

// extract unpacks a tarball into target, which must not exist. It unpacks beside the target and
// renames, so an interrupted download never leaves a half-tree in the cache for the next run to
// mistake for a complete one.
func extract(archive, target string) error {
	if err := os.MkdirAll(filepath.Dir(target), 0o755); err != nil {
		return err
	}

	staging, err := os.MkdirTemp(filepath.Dir(target), ".extract-")
	if err != nil {
		return err
	}
	defer os.RemoveAll(staging)

	if err := unpack(archive, staging); err != nil {
		return err
	}

	root, err := contentRoot(staging)
	if err != nil {
		return err
	}
	return os.Rename(root, target)
}

func unpack(archive, into string) error {
	file, err := os.Open(archive)
	if err != nil {
		return err
	}
	defer file.Close()

	zipped, err := gzip.NewReader(file)
	if err != nil {
		return fmt.Errorf("%s is not a gzip archive: %w", filepath.Base(archive), err)
	}
	defer zipped.Close()

	remaining := int64(maxArchiveBytes)
	reader := tar.NewReader(zipped)
	for {
		header, err := reader.Next()
		if err == io.EOF {
			return nil
		}
		if err != nil {
			return err
		}

		path, err := safeJoin(into, header.Name)
		if err != nil {
			return err
		}
		switch header.Typeflag {
		case tar.TypeDir:
			if err := os.MkdirAll(path, 0o755); err != nil {
				return err
			}
		case tar.TypeReg:
			written, err := writeEntry(path, reader, header, remaining)
			if err != nil {
				return err
			}
			remaining -= written
			if remaining <= 0 {
				return fmt.Errorf("%s expands past %d bytes", filepath.Base(archive), int64(maxArchiveBytes))
			}
		default:
			// A symlink or a device node in an archive is either a mistake or a way out of the
			// directory it is being unpacked into. Neither belongs in a template.
			return fmt.Errorf("%s: unsupported entry type %q", header.Name, header.Typeflag)
		}
	}
}

func writeEntry(path string, reader io.Reader, header *tar.Header, limit int64) (int64, error) {
	if err := os.MkdirAll(filepath.Dir(path), 0o755); err != nil {
		return 0, err
	}

	file, err := os.OpenFile(path, os.O_CREATE|os.O_EXCL|os.O_WRONLY, mode(header))
	if err != nil {
		return 0, err
	}
	defer file.Close()

	return io.Copy(file, io.LimitReader(reader, limit))
}

// mode keeps the executable bit and nothing else: an archive is not the authority on who may read
// the files it carries.
func mode(header *tar.Header) os.FileMode {
	if header.FileInfo().Mode().Perm()&0o111 != 0 {
		return 0o755
	}
	return 0o644
}

// safeJoin refuses any entry that would land outside the directory being unpacked into.
func safeJoin(root, name string) (string, error) {
	cleaned := filepath.Clean(filepath.FromSlash(name))
	if filepath.IsAbs(cleaned) || cleaned == ".." || strings.HasPrefix(cleaned, ".."+string(filepath.Separator)) {
		return "", fmt.Errorf("archive entry %q escapes the directory it unpacks into", name)
	}
	return filepath.Join(root, cleaned), nil
}

// contentRoot unwraps the single top-level directory an archive built with `tar czf x.tar.gz repo/`
// carries, and leaves an archive packed at its root alone.
func contentRoot(staging string) (string, error) {
	entries, err := os.ReadDir(staging)
	if err != nil {
		return "", err
	}
	if len(entries) == 0 {
		return "", fmt.Errorf("the archive is empty")
	}
	if len(entries) == 1 && entries[0].IsDir() {
		return filepath.Join(staging, entries[0].Name()), nil
	}
	return staging, nil
}

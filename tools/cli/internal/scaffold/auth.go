package scaffold

import (
	"bytes"
	"encoding/json"
	"fmt"
	"os"
	"path/filepath"

	"github.com/SekiroKenjii/kakehashi/tools/cli/internal/template"
)

// setAuthMode writes the chosen sign-in mode into the client's settings file. The template names
// the file and the key, because which settings file carries the mode is the template's business
// and it changes with the template rather than with this binary.
func setAuthMode(root string, d *template.Descriptor, in Inputs) error {
	if d.Auth == nil {
		if in.Auth != AuthInApp {
			return fmt.Errorf("this template version does not support --auth")
		}
		return nil
	}

	value, ok := d.Auth.Modes[in.Auth]
	if !ok {
		return fmt.Errorf("this template version does not support --auth %s", in.Auth)
	}
	if len(d.Auth.Path) == 0 {
		return fmt.Errorf("%s: the auth setting names no key", template.DescriptorName)
	}

	path := filepath.Join(root, filepath.FromSlash(d.Auth.File))
	body, err := os.ReadFile(path)
	if err != nil {
		return err
	}

	// UseNumber, or a port number comes back out as 8080 in exponent form.
	var settings map[string]any
	decoder := json.NewDecoder(bytes.NewReader(body))
	decoder.UseNumber()
	if err := decoder.Decode(&settings); err != nil {
		return fmt.Errorf("parse %s: %w", d.Auth.File, err)
	}

	current, err := set(settings, d.Auth.Path, value)
	if err != nil {
		return fmt.Errorf("%s: %w", d.Auth.File, err)
	}
	if current == value {
		return nil
	}

	next, err := json.MarshalIndent(settings, "", "  ")
	if err != nil {
		return err
	}
	info, err := os.Stat(path)
	if err != nil {
		return err
	}
	return os.WriteFile(path, append(next, '\n'), info.Mode().Perm())
}

// set walks the key path and replaces the leaf, returning what was there before. Every key on the
// way has to exist: creating one would mean writing a setting the application does not read.
func set(node map[string]any, keys []string, value string) (string, error) {
	for _, key := range keys[:len(keys)-1] {
		child, ok := node[key].(map[string]any)
		if !ok {
			return "", fmt.Errorf("no object at %q", key)
		}
		node = child
	}

	leaf := keys[len(keys)-1]
	current, ok := node[leaf].(string)
	if !ok {
		return "", fmt.Errorf("no string at %q", leaf)
	}
	node[leaf] = value
	return current, nil
}

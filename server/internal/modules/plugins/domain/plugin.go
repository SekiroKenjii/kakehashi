// Package domain holds the plugins module's entities and the rules they enforce.
//
// It is the innermost layer: it imports the platform's error types and nothing else. No SQL, no
// protobuf, no other module. That is what makes the rules in here testable without standing up a
// database or a server.
//
// One aggregate root, Plugin, with the versions published under it. A version is not a root of its
// own: a version only means anything under the plugin it belongs to, and the rules that matter —
// an identity that never changes, a version number that never repeats — are rules about the set.
package domain

import (
	"regexp"
	"strings"
	"time"

	"__GO_MODULE__/server/internal/platform/errs"
	"__GO_MODULE__/server/internal/platform/text"
)

// MaxDisplayNameLength caps the name a catalog row shows.
const MaxDisplayNameLength = 120

// MaxDescriptionLength caps the sentence under it.
const MaxDescriptionLength = 400

var (
	// A catalog identity is lower case, digits and single hyphens: it is shown in a monospace
	// column, typed into a command, and used as a directory name on the client.
	pluginIDPattern = regexp.MustCompile(`^[a-z0-9]+(-[a-z0-9]+)*$`)

	// major.minor.patch. Pre-release tags are deliberately absent: ordering them correctly is a
	// specification of its own, and a catalog that sorts them wrongly offers the wrong update.
	versionPattern = regexp.MustCompile(`^\d+\.\d+\.\d+$`)

	// major.minor, matching what the client stamps its assemblies with.
	hostSDKPattern = regexp.MustCompile(`^\d+\.\d+$`)

	sha256Pattern = regexp.MustCompile(`^[a-f0-9]{64}$`)
)

// Plugin is the aggregate root.
//
// Its fields are exported for the store to scan into, but construction goes through NewPlugin,
// which is where the invariants live.
type Plugin struct {
	PluginID    string
	DisplayName string
	Description string
	Publisher   string
	IsListed    bool
	CreatedAt   time.Time
	UpdatedAt   time.Time
}

// Version is one package published under a plugin.
type Version struct {
	PluginID    string
	Version     string
	MinHostSDK  string
	SizeInBytes int64
	SHA256      string
	IsYanked    bool
	PublishedAt time.Time
}

// NewPlugin builds a valid plugin, or explains why it cannot.
//
// now is passed in rather than read from the clock so tests can pin it.
func NewPlugin(pluginID, displayName, description, publisher string, now time.Time) (Plugin, error) {
	pluginID, err := normalizePluginID(pluginID)
	if err != nil {
		return Plugin{}, err
	}

	displayName, err = normalizeText(displayName, "display name", MaxDisplayNameLength, true)
	if err != nil {
		return Plugin{}, err
	}

	description, err = normalizeText(description, "description", MaxDescriptionLength, false)
	if err != nil {
		return Plugin{}, err
	}

	return Plugin{
		PluginID:    pluginID,
		DisplayName: displayName,
		Description: description,
		Publisher:   strings.TrimSpace(publisher),
		IsListed:    true,
		CreatedAt:   now,
		UpdatedAt:   now,
	}, nil
}

// NewVersion builds a valid version of a plugin, or explains why it cannot.
//
// size is checked against maxBytes here rather than at the wire, because the rule is about what
// the catalog will hold and every caller has to obey it.
func NewVersion(
	pluginID, version, minHostSDK, sha256 string, size, maxBytes int64, now time.Time,
) (Version, error) {
	pluginID, err := normalizePluginID(pluginID)
	if err != nil {
		return Version{}, err
	}

	version = strings.TrimSpace(version)
	if !versionPattern.MatchString(version) {
		return Version{}, errs.Invalidf("A version reads major.minor.patch, not %q.", version)
	}

	minHostSDK = strings.TrimSpace(minHostSDK)
	if !hostSDKPattern.MatchString(minHostSDK) {
		return Version{}, errs.Invalidf("A host SDK version reads major.minor, not %q.", minHostSDK)
	}

	sha256 = strings.ToLower(strings.TrimSpace(sha256))
	if !sha256Pattern.MatchString(sha256) {
		return Version{}, errs.Invalidf("A digest is 64 hexadecimal characters.")
	}

	if size <= 0 {
		return Version{}, errs.Invalidf("A package cannot be empty.")
	}
	if size > maxBytes {
		return Version{}, errs.Invalidf("A package is limited to %d bytes.", maxBytes)
	}

	return Version{
		PluginID:    pluginID,
		Version:     version,
		MinHostSDK:  minHostSDK,
		SizeInBytes: size,
		SHA256:      sha256,
		PublishedAt: now,
	}, nil
}

// Describe rewrites what the catalog says about a plugin, keeping the invariants.
func (p *Plugin) Describe(displayName, description, publisher string, now time.Time) error {
	displayName, err := normalizeText(displayName, "display name", MaxDisplayNameLength, true)
	if err != nil {
		return err
	}

	description, err = normalizeText(description, "description", MaxDescriptionLength, false)
	if err != nil {
		return err
	}
	p.DisplayName = displayName
	p.Description = description
	p.Publisher = strings.TrimSpace(publisher)
	p.UpdatedAt = now
	return nil
}

// ValidatePluginID answers whether a caller-supplied identity is one this catalog can hold. It is
// exported because a lookup has to refuse a malformed identity without building an entity first.
func ValidatePluginID(pluginID string) error {
	_, err := normalizePluginID(pluginID)
	return err
}

func normalizePluginID(pluginID string) (string, error) {
	pluginID = strings.TrimSpace(pluginID)

	if !pluginIDPattern.MatchString(pluginID) {
		return "", errs.Invalidf(
			"A plugin id is lower case, digits and single hyphens, not %q.", pluginID)
	}
	return pluginID, nil
}

func normalizeText(value, label string, max int, required bool) (string, error) {
	value = strings.TrimSpace(value)

	if required && value == "" {
		return "", errs.Invalidf("A plugin needs a %s.", label)
	}
	// Runes, not bytes: len() lets a Vietnamese name through at 40 characters and rejects an
	// English one at 121.
	if text.UTF16Len(value) > max {
		return "", errs.Invalidf("A %s is limited to %d characters.", label, max)
	}
	return value, nil
}

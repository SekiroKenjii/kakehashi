// Package pluginsapi is the plugins module's public contract.
//
// Other modules import this package and nothing else under internal/modules/plugins/. Keep it free
// of implementation: interfaces, plain data, events. No SQL, no protobuf, no other module.
package pluginsapi

import (
	"context"
	"io"
	"time"
)

// PermissionManagePlugins guards publishing and withdrawing. Reading the catalog needs only the
// module's own access, which every gated route already requires.
const PermissionManagePlugins = "plugins.manage"

// Install sources a client may report. The set is closed because it is a claim about provenance,
// and an open one would let a client invent a source a reader would trust.
const (
	SourceCatalog = "Catalog"
	SourceURL     = "Url"
	SourceFile    = "File"
)

// MaxPackageBytes is the largest artifact this catalog accepts.
//
// A ceiling rather than a setting: it bounds what one upload can cost the database and what one
// download can cost a client, and a deployment that needs a different number is changing a
// contract rather than a preference.
const MaxPackageBytes = 64 << 20

// Plugin is one offering in the catalog.
type Plugin struct {
	// PluginID is the catalog identity, stable across versions.
	PluginID    string
	DisplayName string
	Description string

	// Publisher is what the deployment says about who publishes this. It is not a signature: what
	// signed the assemblies travels on the artifact, and the client is what checks it.
	Publisher string

	IsListed  bool
	CreatedAt time.Time
	UpdatedAt time.Time
}

// Version is one published package.
type Version struct {
	PluginID    string
	Version     string
	MinHostSDK  string
	SizeInBytes int64

	// SHA256 is the lower-case hex hash of the whole archive.
	SHA256 string

	// IsYanked withdraws a version from the catalog without deleting it, so an account that
	// already installed it is still explainable.
	IsYanked    bool
	PublishedAt time.Time
}

// Listing is a plugin and the newest version still on offer for it.
type Listing struct {
	Plugin Plugin
	Latest Version
}

// Service is the plugins use-case surface.
type Service interface {
	// List returns the listed plugins that have at least one version on offer.
	List(ctx context.Context) ([]Listing, error)

	// Get returns one plugin and its versions, newest first. It fails with an errs.NotFound error
	// when pluginID does not exist.
	Get(ctx context.Context, pluginID string) (Plugin, []Version, error)

	// Download writes one version's bytes to w.
	Download(ctx context.Context, pluginID, version string, w io.Writer) error

	// RecordInstall stores that userID installed a version. A version this catalog does not have
	// fails with an errs.Invalid error, which is what stops a client asserting a package nobody
	// published.
	RecordInstall(ctx context.Context, userID, pluginID, version, source string) error

	// Publish stores an uploaded package. A digest that disagrees with the bytes received, or a
	// package over MaxPackageBytes, fails with an errs.Invalid error.
	Publish(ctx context.Context, plugin Plugin, version Version, content []byte) (Version, error)

	// SetYanked withdraws a version, or puts it back.
	SetYanked(ctx context.Context, pluginID, version string, yanked bool) error

	// SetListed shows or hides a whole plugin.
	SetListed(ctx context.Context, pluginID string, listed bool) error
}

// Installed is published after a client reports installing a version.
//
// It carries the source because that is the difference a reader acts on: a package from this
// catalog was vetted by whoever published it, and one from a file was not.
type Installed struct {
	UserID   string
	PluginID string
	Version  string
	Source   string
	At       time.Time
}

// Published is published after a new version enters the catalog.
type Published struct {
	PluginID string
	Version  string
	At       time.Time
}

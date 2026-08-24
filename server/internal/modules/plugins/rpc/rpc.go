// Package rpc is the plugins module's wire layer.
//
// It is the only package in the module allowed to import the generated protobuf code, and
// tools/archlint enforces that. Everything here is mapping: no rules, no decisions, no error
// shaping. Errors are returned as the service produced them, and the interceptor in platform/rpc
// turns them into status codes.
//
// The files: this one is the assembly and the shared mapping, catalog.go is the surface a
// signed-in client may reach, and admin.go is the surface that changes what is on offer.
package rpc

import (
	"net/http"

	"connectrpc.com/connect"
	"google.golang.org/protobuf/types/known/timestamppb"

	pluginsv1 "__GO_MODULE__/server/internal/gen/__PROTO_PACKAGE__/plugins/v1"
	"__GO_MODULE__/server/internal/gen/__PROTO_PACKAGE__/plugins/v1/pluginsv1connect"
	pluginsapi "__GO_MODULE__/server/internal/modules/plugins/api"
)

// NewRoute builds the Connect handler for PluginService.
func NewRoute(svc pluginsapi.Service, opts []connect.HandlerOption) (string, http.Handler) {
	return pluginsv1connect.NewPluginServiceHandler(&handler{svc: svc}, opts...)
}

// NewAdminRoute builds the Connect handler for PluginAdminService.
func NewAdminRoute(svc pluginsapi.Service, opts []connect.HandlerOption) (string, http.Handler) {
	return pluginsv1connect.NewPluginAdminServiceHandler(&adminHandler{svc: svc}, opts...)
}

// handler adapts pluginsapi.Service to the generated read surface.
type handler struct {
	svc pluginsapi.Service
}

// adminHandler adapts pluginsapi.Service to the generated write surface.
type adminHandler struct {
	svc pluginsapi.Service
}

func toProtoPlugin(p pluginsapi.Plugin) *pluginsv1.Plugin {
	return &pluginsv1.Plugin{
		PluginId:    p.PluginID,
		DisplayName: p.DisplayName,
		Description: p.Description,
		Publisher:   p.Publisher,
		IsListed:    p.IsListed,
		CreatedAt:   timestamppb.New(p.CreatedAt),
		UpdatedAt:   timestamppb.New(p.UpdatedAt),
	}
}

func toProtoVersion(v pluginsapi.Version) *pluginsv1.PluginVersion {
	return &pluginsv1.PluginVersion{
		PluginId:    v.PluginID,
		Version:     v.Version,
		MinHostSdk:  v.MinHostSDK,
		SizeInBytes: v.SizeInBytes,
		Sha256:      v.SHA256,
		IsYanked:    v.IsYanked,
		PublishedAt: timestamppb.New(v.PublishedAt),
	}
}

// fromProtoSource maps the wire's enum onto the module's vocabulary. An unrecognised value becomes
// the empty string, which the service refuses: mapping it to a default would silently relabel a
// sideloaded package as one this catalog vetted.
func fromProtoSource(source pluginsv1.InstallSource) string {
	switch source {
	case pluginsv1.InstallSource_INSTALL_SOURCE_CATALOG:
		return pluginsapi.SourceCatalog
	case pluginsv1.InstallSource_INSTALL_SOURCE_URL:
		return pluginsapi.SourceURL
	case pluginsv1.InstallSource_INSTALL_SOURCE_FILE:
		return pluginsapi.SourceFile
	default:
		return ""
	}
}

var (
	_ pluginsv1connect.PluginServiceHandler      = (*handler)(nil)
	_ pluginsv1connect.PluginAdminServiceHandler = (*adminHandler)(nil)
)

package rpc

import (
	"context"

	"connectrpc.com/connect"

	pluginsv1 "__GO_MODULE__/server/internal/gen/__PROTO_PACKAGE__/plugins/v1"
	"__GO_MODULE__/server/internal/platform/auth"
	"__GO_MODULE__/server/internal/platform/errs"
)

// The surface a signed-in client may reach.

func (h *handler) ListPlugins(
	ctx context.Context, _ *connect.Request[pluginsv1.ListPluginsRequest],
) (*connect.Response[pluginsv1.ListPluginsResponse], error) {
	listings, err := h.svc.List(ctx)
	if err != nil {
		return nil, err
	}

	plugins := make([]*pluginsv1.Plugin, len(listings))
	latest := make([]*pluginsv1.PluginVersion, len(listings))
	for i, l := range listings {
		plugins[i] = toProtoPlugin(l.Plugin)
		latest[i] = toProtoVersion(l.Latest)
	}
	return connect.NewResponse(&pluginsv1.ListPluginsResponse{Plugins: plugins, Latest: latest}), nil
}

func (h *handler) GetPlugin(
	ctx context.Context, req *connect.Request[pluginsv1.GetPluginRequest],
) (*connect.Response[pluginsv1.GetPluginResponse], error) {
	plugin, versions, err := h.svc.Get(ctx, req.Msg.GetPluginId())
	if err != nil {
		return nil, err
	}

	out := make([]*pluginsv1.PluginVersion, len(versions))
	for i, v := range versions {
		out[i] = toProtoVersion(v)
	}
	return connect.NewResponse(&pluginsv1.GetPluginResponse{
		Plugin:   toProtoPlugin(plugin),
		Versions: out,
	}), nil
}

func (h *handler) DownloadPluginVersion(
	ctx context.Context,
	req *connect.Request[pluginsv1.DownloadPluginVersionRequest],
	stream *connect.ServerStream[pluginsv1.DownloadPluginVersionResponse],
) error {
	return h.svc.Download(ctx, req.Msg.GetPluginId(), req.Msg.GetVersion(), &chunkWriter{stream: stream})
}

func (h *handler) ReportInstalled(
	ctx context.Context, req *connect.Request[pluginsv1.ReportInstalledRequest],
) (*connect.Response[pluginsv1.ReportInstalledResponse], error) {
	// Whose install this is comes from the token, never from the request: a client that could name
	// the account could write a row into somebody else's history.
	subject, ok := auth.SubjectFrom(ctx)
	if !ok {
		return nil, errs.Unauthenticatedf("Sign in to report an install.")
	}

	err := h.svc.RecordInstall(
		ctx, subject.ID, req.Msg.GetPluginId(), req.Msg.GetVersion(),
		fromProtoSource(req.Msg.GetSource()))
	if err != nil {
		return nil, err
	}
	return connect.NewResponse(&pluginsv1.ReportInstalledResponse{}), nil
}

// chunkWriter turns the service's io.Writer into stream messages, so nothing between the database
// and the socket holds a whole package.
type chunkWriter struct {
	stream *connect.ServerStream[pluginsv1.DownloadPluginVersionResponse]
}

func (w *chunkWriter) Write(p []byte) (int, error) {
	if err := w.stream.Send(&pluginsv1.DownloadPluginVersionResponse{Chunk: p}); err != nil {
		return 0, err
	}
	return len(p), nil
}

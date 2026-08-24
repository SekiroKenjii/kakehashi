package rpc

import (
	"context"

	"connectrpc.com/connect"

	pluginsv1 "__GO_MODULE__/server/internal/gen/__PROTO_PACKAGE__/plugins/v1"
	pluginsapi "__GO_MODULE__/server/internal/modules/plugins/api"
	"__GO_MODULE__/server/internal/platform/errs"
)

// The surface that changes what the catalog offers.

func (h *adminHandler) PublishPluginVersion(
	ctx context.Context, stream *connect.ClientStream[pluginsv1.PublishPluginVersionRequest],
) (*connect.Response[pluginsv1.PublishPluginVersionResponse], error) {
	var (
		header  *pluginsv1.PublishPluginVersionHeader
		content []byte
	)

	for stream.Receive() {
		switch payload := stream.Msg().GetPayload().(type) {
		case *pluginsv1.PublishPluginVersionRequest_Header:
			if header != nil {
				return nil, errs.Invalidf("An upload carries one header.")
			}
			header = payload.Header
		case *pluginsv1.PublishPluginVersionRequest_Chunk:
			if header == nil {
				return nil, errs.Invalidf("An upload starts with its header.")
			}
			// Counted as it arrives rather than after: a sender that ignores the limit should be
			// refused before the whole package is in memory, not once it already is.
			if int64(len(content))+int64(len(payload.Chunk)) > pluginsapi.MaxPackageBytes {
				return nil, errs.Invalidf(
					"A package is limited to %d bytes.", int64(pluginsapi.MaxPackageBytes))
			}
			content = append(content, payload.Chunk...)
		}
	}
	if err := stream.Err(); err != nil {
		return nil, err
	}
	if header == nil {
		return nil, errs.Invalidf("An upload starts with its header.")
	}

	version, err := h.svc.Publish(
		ctx,
		pluginsapi.Plugin{
			PluginID:    header.GetPluginId(),
			DisplayName: header.GetDisplayName(),
			Description: header.GetDescription(),
			Publisher:   header.GetPublisher(),
		},
		pluginsapi.Version{
			PluginID:   header.GetPluginId(),
			Version:    header.GetVersion(),
			MinHostSDK: header.GetMinHostSdk(),
			SHA256:     header.GetSha256(),
		},
		content)
	if err != nil {
		return nil, err
	}
	return connect.NewResponse(&pluginsv1.PublishPluginVersionResponse{
		Version: toProtoVersion(version),
	}), nil
}

func (h *adminHandler) YankPluginVersion(
	ctx context.Context, req *connect.Request[pluginsv1.YankPluginVersionRequest],
) (*connect.Response[pluginsv1.YankPluginVersionResponse], error) {
	err := h.svc.SetYanked(ctx, req.Msg.GetPluginId(), req.Msg.GetVersion(), req.Msg.GetIsYanked())
	if err != nil {
		return nil, err
	}
	return connect.NewResponse(&pluginsv1.YankPluginVersionResponse{}), nil
}

func (h *adminHandler) SetPluginListed(
	ctx context.Context, req *connect.Request[pluginsv1.SetPluginListedRequest],
) (*connect.Response[pluginsv1.SetPluginListedResponse], error) {
	if err := h.svc.SetListed(ctx, req.Msg.GetPluginId(), req.Msg.GetIsListed()); err != nil {
		return nil, err
	}
	return connect.NewResponse(&pluginsv1.SetPluginListedResponse{}), nil
}

// Package rpc is the only package in the module allowed to import the generated protobuf code, and
// tools/archlint enforces that. Mapping only: no rules, no decisions, no error shaping. Errors go
// back as the service produced them, and the interceptor in platform/rpc turns them into status
// codes.
package rpc

import (
	"context"
	"net/http"

	"connectrpc.com/connect"
	"google.golang.org/protobuf/types/known/timestamppb"

	notesv1 "github.com/SekiroKenjii/kakehashi/server/internal/gen/kakehashi/notes/v1"
	"github.com/SekiroKenjii/kakehashi/server/internal/gen/kakehashi/notes/v1/notesv1connect"
	notesapi "github.com/SekiroKenjii/kakehashi/server/internal/modules/notes/api"
)

func NewRoute(svc notesapi.Service, opts []connect.HandlerOption) (string, http.Handler) {
	return notesv1connect.NewNotesServiceHandler(&handler{svc: svc}, opts...)
}

type handler struct {
	svc notesapi.Service
}

func (h *handler) ListNotes(
	ctx context.Context, _ *connect.Request[notesv1.ListNotesRequest],
) (*connect.Response[notesv1.ListNotesResponse], error) {
	notes, err := h.svc.List(ctx)
	if err != nil {
		return nil, err
	}

	out := make([]*notesv1.Note, len(notes))
	for i, n := range notes {
		out[i] = toProto(n)
	}
	return connect.NewResponse(&notesv1.ListNotesResponse{Notes: out}), nil
}

func (h *handler) GetNote(
	ctx context.Context, req *connect.Request[notesv1.GetNoteRequest],
) (*connect.Response[notesv1.GetNoteResponse], error) {
	note, err := h.svc.Get(ctx, req.Msg.GetId())
	if err != nil {
		return nil, err
	}
	return connect.NewResponse(&notesv1.GetNoteResponse{Note: toProto(note)}), nil
}

func (h *handler) CreateNote(
	ctx context.Context, req *connect.Request[notesv1.CreateNoteRequest],
) (*connect.Response[notesv1.CreateNoteResponse], error) {
	note, err := h.svc.Create(ctx, req.Msg.GetTitle(), req.Msg.GetBody())
	if err != nil {
		return nil, err
	}
	return connect.NewResponse(&notesv1.CreateNoteResponse{Note: toProto(note)}), nil
}

func (h *handler) UpdateNote(
	ctx context.Context, req *connect.Request[notesv1.UpdateNoteRequest],
) (*connect.Response[notesv1.UpdateNoteResponse], error) {
	note, err := h.svc.Update(ctx, req.Msg.GetId(), req.Msg.GetTitle(), req.Msg.GetBody())
	if err != nil {
		return nil, err
	}
	return connect.NewResponse(&notesv1.UpdateNoteResponse{Note: toProto(note)}), nil
}

func (h *handler) DeleteNote(
	ctx context.Context, req *connect.Request[notesv1.DeleteNoteRequest],
) (*connect.Response[notesv1.DeleteNoteResponse], error) {
	if err := h.svc.Delete(ctx, req.Msg.GetId()); err != nil {
		return nil, err
	}
	return connect.NewResponse(&notesv1.DeleteNoteResponse{}), nil
}

func toProto(n notesapi.Note) *notesv1.Note {
	return &notesv1.Note{
		Id:        n.ID,
		Title:     n.Title,
		Body:      n.Body,
		CreatedAt: timestamppb.New(n.CreatedAt),
		UpdatedAt: timestamppb.New(n.UpdatedAt),
	}
}

var _ notesv1connect.NotesServiceHandler = (*handler)(nil)

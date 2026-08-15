using System;
using System.Threading;
using System.Threading.Tasks;
using __ROOT_NAMESPACE__.Application.Abstractions.Messaging;
using __ROOT_NAMESPACE__.Modules.Notes.Application.Abstractions;
using __ROOT_NAMESPACE__.Modules.Notes.Domain.Notes;
using __ROOT_NAMESPACE__.SharedKernel;

namespace __ROOT_NAMESPACE__.Modules.Notes.Application.Notes.Commands.UpdateNote;

/// <summary>Validates the draft locally, then asks the server to rewrite the note.</summary>
public sealed class UpdateNoteCommandHandler
    : IRequestHandler<UpdateNoteCommand, Result<NoteDto>>
{
    private readonly INotesGateway _notes;

    public UpdateNoteCommandHandler(INotesGateway notes)
    {
        ArgumentNullException.ThrowIfNull(notes);
        _notes = notes;
    }

    public async Task<Result<NoteDto>> Handle(
        UpdateNoteCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var draft = NoteDraft.Create(request.Title, request.Body);

        if (draft.IsFailure)
        {
            return Result.Failure<NoteDto>(draft.Error);
        }

        return await _notes.UpdateAsync(request.Id, draft.Value, cancellationToken)
            .ConfigureAwait(false);
    }
}

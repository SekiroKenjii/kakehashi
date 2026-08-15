using System;
using System.Threading;
using System.Threading.Tasks;
using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.Modules.Notes.Application.Abstractions;
using Kakehashi.Modules.Notes.Domain.Notes;
using Kakehashi.SharedKernel;

namespace Kakehashi.Modules.Notes.Application.Notes.Commands.CreateNote;

/// <summary>Validates the draft locally, then asks the server to store it.</summary>
public sealed class CreateNoteCommandHandler
    : IRequestHandler<CreateNoteCommand, Result<NoteDto>>
{
    private readonly INotesGateway _notes;

    public CreateNoteCommandHandler(INotesGateway notes)
    {
        ArgumentNullException.ThrowIfNull(notes);
        _notes = notes;
    }

    public async Task<Result<NoteDto>> Handle(
        CreateNoteCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Fail here and the user sees the problem immediately, on the keystroke rather than after a
        // round trip. The server checks again regardless — this is a courtesy, not a gate.
        var draft = NoteDraft.Create(request.Title, request.Body);

        if (draft.IsFailure)
        {
            return Result.Failure<NoteDto>(draft.Error);
        }

        return await _notes.CreateAsync(draft.Value, cancellationToken).ConfigureAwait(false);
    }
}

using System;
using System.Threading;
using System.Threading.Tasks;
using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.Modules.Notes.Application.Abstractions;
using Kakehashi.Modules.Notes.Domain.Notes;
using Kakehashi.SharedKernel;

namespace Kakehashi.Modules.Notes.Application.Notes.Commands.UpdateNote {
  public sealed class UpdateNoteCommandHandler
      : IRequestHandler<UpdateNoteCommand, Result<NoteDto>> {
    private readonly INotesGateway _notes;

    public UpdateNoteCommandHandler(INotesGateway notes) {
      ArgumentNullException.ThrowIfNull(notes);
      _notes = notes;
    }

    public async Task<Result<NoteDto>> Handle(
        UpdateNoteCommand request, CancellationToken cancellationToken) {
      ArgumentNullException.ThrowIfNull(request);

      var draft = NoteDraft.Create(request.Title, request.Body);
      if (draft.IsFailure) {
        return Result.Failure<NoteDto>(draft.Error);
      }

      return await _notes.UpdateAsync(request.Id, draft.Value, cancellationToken)
          .ConfigureAwait(false);
    }
  }
}

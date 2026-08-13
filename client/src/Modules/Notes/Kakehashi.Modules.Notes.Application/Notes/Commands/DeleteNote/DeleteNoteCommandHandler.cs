using System;
using System.Threading;
using System.Threading.Tasks;
using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.Modules.Notes.Application.Abstractions;
using Kakehashi.SharedKernel;

namespace Kakehashi.Modules.Notes.Application.Notes.Commands.DeleteNote {
  public sealed class DeleteNoteCommandHandler : IRequestHandler<DeleteNoteCommand, Result> {
    private readonly INotesGateway _notes;

    public DeleteNoteCommandHandler(INotesGateway notes) {
      ArgumentNullException.ThrowIfNull(notes);
      _notes = notes;
    }

    /// <summary>Deletes the note, without validating the id first.</summary>
    /// <remarks>
    /// An id either names a note or does not, and only the server knows which. It treats deleting
    /// something already gone as success, so a retry after a dropped connection does not surface
    /// as an error the user has to interpret.
    /// </remarks>
    public Task<Result> Handle(DeleteNoteCommand request, CancellationToken cancellationToken) {
      ArgumentNullException.ThrowIfNull(request);
      return _notes.DeleteAsync(request.Id, cancellationToken);
    }
  }
}

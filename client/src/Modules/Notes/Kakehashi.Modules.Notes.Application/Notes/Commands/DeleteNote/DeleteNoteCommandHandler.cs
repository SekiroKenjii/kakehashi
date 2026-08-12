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

    // Nothing to validate: only the server knows whether an id names a note. It treats a delete of
    // something already gone as success, so a retry after a dropped connection is not an error.
    public Task<Result> Handle(DeleteNoteCommand request, CancellationToken cancellationToken) {
      ArgumentNullException.ThrowIfNull(request);
      return _notes.DeleteAsync(request.Id, cancellationToken);
    }
  }
}

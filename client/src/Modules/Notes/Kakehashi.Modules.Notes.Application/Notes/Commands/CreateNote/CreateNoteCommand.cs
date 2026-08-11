using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.SharedKernel;

namespace Kakehashi.Modules.Notes.Application.Notes.Commands.CreateNote {
  // Creates a note.
  // Title: Required; trimmed before it is sent.
  // Body: Optional.
  public sealed record CreateNoteCommand(string? Title, string? Body)
      : IRequest<Result<NoteDto>>;
}

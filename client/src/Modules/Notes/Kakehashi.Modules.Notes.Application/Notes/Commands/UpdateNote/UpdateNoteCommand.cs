using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.SharedKernel;

namespace Kakehashi.Modules.Notes.Application.Notes.Commands.UpdateNote {
  // Rewrites a note's title and body.
  // Id: The note to rewrite.
  // Title: Required; trimmed before it is sent.
  // Body: Optional.
  public sealed record UpdateNoteCommand(long Id, string? Title, string? Body)
      : IRequest<Result<NoteDto>>;
}

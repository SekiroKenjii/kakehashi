using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.SharedKernel;

namespace Kakehashi.Modules.Notes.Application.Notes.Commands.UpdateNote {
  public sealed record UpdateNoteCommand(long Id, string? Title, string? Body)
      : IRequest<Result<NoteDto>>;
}

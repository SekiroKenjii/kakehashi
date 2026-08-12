using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.SharedKernel;

namespace Kakehashi.Modules.Notes.Application.Notes.Commands.CreateNote {
  public sealed record CreateNoteCommand(string? Title, string? Body)
      : IRequest<Result<NoteDto>>;
}

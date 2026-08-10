using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.SharedKernel;

namespace Kakehashi.Modules.Notes.Application.Notes.Commands.CreateNote {
  /// <summary>Creates a note.</summary>
  /// <param name="Title">Required; trimmed before it is sent.</param>
  /// <param name="Body">Optional.</param>
  public sealed record CreateNoteCommand(string? Title, string? Body)
      : IRequest<Result<NoteDto>>;
}

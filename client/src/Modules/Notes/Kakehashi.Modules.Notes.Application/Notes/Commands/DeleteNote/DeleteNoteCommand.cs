using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.SharedKernel;

namespace Kakehashi.Modules.Notes.Application.Notes.Commands.DeleteNote {
  // Removes a note. Removing one that is already gone succeeds.
  public sealed record DeleteNoteCommand(long Id) : IRequest<Result>;
}

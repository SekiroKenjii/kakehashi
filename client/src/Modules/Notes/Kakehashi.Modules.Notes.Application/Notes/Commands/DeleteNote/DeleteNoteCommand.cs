using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.SharedKernel;

namespace Kakehashi.Modules.Notes.Application.Notes.Commands.DeleteNote {
  public sealed record DeleteNoteCommand(long Id) : IRequest<Result>;
}

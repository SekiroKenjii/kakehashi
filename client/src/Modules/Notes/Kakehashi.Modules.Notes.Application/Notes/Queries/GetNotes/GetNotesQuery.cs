using System.Collections.Generic;
using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.SharedKernel;

namespace Kakehashi.Modules.Notes.Application.Notes.Queries.GetNotes {
  // Ordered most recently updated first.
  public sealed record GetNotesQuery : IRequest<Result<IReadOnlyList<NoteDto>>>;
}

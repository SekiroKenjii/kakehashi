using System.Collections.Generic;
using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.SharedKernel;

namespace Kakehashi.Modules.Notes.Application.Notes.Queries.GetNotes {
  /// <summary>Lists every note, most recently updated first.</summary>
  public sealed record GetNotesQuery : IRequest<Result<IReadOnlyList<NoteDto>>>;
}

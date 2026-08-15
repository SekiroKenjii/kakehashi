using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.SharedKernel;

namespace Kakehashi.Modules.Notes.Application.Notes.Commands.UpdateNote;

/// <summary>Rewrites a note's title and body.</summary>
/// <param name="Id">The note to rewrite.</param>
/// <param name="Title">Required; trimmed before it is sent.</param>
/// <param name="Body">Optional.</param>
public sealed record UpdateNoteCommand(long Id, string? Title, string? Body)
    : IRequest<Result<NoteDto>>;

using __ROOT_NAMESPACE__.Application.Abstractions.Messaging;
using __ROOT_NAMESPACE__.SharedKernel;

namespace __ROOT_NAMESPACE__.Modules.Notes.Application.Notes.Commands.CreateNote;

/// <summary>Creates a note.</summary>
/// <param name="Title">Required; trimmed before it is sent.</param>
/// <param name="Body">Optional.</param>
public sealed record CreateNoteCommand(string? Title, string? Body)
    : IRequest<Result<NoteDto>>;

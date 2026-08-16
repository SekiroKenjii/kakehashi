using __ROOT_NAMESPACE__.Application.Abstractions.Messaging;
using __ROOT_NAMESPACE__.SharedKernel;

namespace __ROOT_NAMESPACE__.Modules.Notes.Application.Notes.Commands.DeleteNote;

/// <summary>Removes a note. Removing one that is already gone succeeds.</summary>
public sealed record DeleteNoteCommand(long Id) : IRequest<Result>;

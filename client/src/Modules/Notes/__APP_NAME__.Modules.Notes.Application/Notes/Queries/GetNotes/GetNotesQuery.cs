using System.Collections.Generic;
using __ROOT_NAMESPACE__.Application.Abstractions.Messaging;
using __ROOT_NAMESPACE__.SharedKernel;

namespace __ROOT_NAMESPACE__.Modules.Notes.Application.Notes.Queries.GetNotes;

/// <summary>Lists every note, most recently updated first.</summary>
public sealed record GetNotesQuery : IRequest<Result<IReadOnlyList<NoteDto>>>;

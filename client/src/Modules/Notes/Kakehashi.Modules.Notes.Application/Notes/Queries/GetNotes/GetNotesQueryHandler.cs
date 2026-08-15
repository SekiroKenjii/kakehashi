using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.Modules.Notes.Application.Abstractions;
using Kakehashi.SharedKernel;

namespace Kakehashi.Modules.Notes.Application.Notes.Queries.GetNotes;

public sealed class GetNotesQueryHandler
    : IRequestHandler<GetNotesQuery, Result<IReadOnlyList<NoteDto>>>
{
    private readonly INotesGateway _notes;

    public GetNotesQueryHandler(INotesGateway notes)
    {
        ArgumentNullException.ThrowIfNull(notes);
        _notes = notes;
    }

    /// <summary>Returns every note.</summary>
    /// <remarks>
    /// A pass-through kept on purpose: view models call this rather than the gateway, so
    /// client-side sorting, filtering or merging has a home that costs no caller a change.
    /// </remarks>
    public Task<Result<IReadOnlyList<NoteDto>>> Handle(
        GetNotesQuery request, CancellationToken cancellationToken)
    {
        return _notes.ListAsync(cancellationToken);
    }
}

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.Modules.Notes.Application.Abstractions;
using Kakehashi.SharedKernel;

namespace Kakehashi.Modules.Notes.Application.Notes.Queries.GetNotes {
  public sealed class GetNotesQueryHandler
      : IRequestHandler<GetNotesQuery, Result<IReadOnlyList<NoteDto>>> {
    private readonly INotesGateway _notes;

    public GetNotesQueryHandler(INotesGateway notes) {
      ArgumentNullException.ThrowIfNull(notes);
      _notes = notes;
    }

    // A pass-through until this query grows a reason not to be — a client-side sort, a filter, a
    // merge with something local. That would live here, which is why the view model talks to the
    // handler rather than to the gateway.
    public Task<Result<IReadOnlyList<NoteDto>>> Handle(
        GetNotesQuery request, CancellationToken cancellationToken) {
      return _notes.ListAsync(cancellationToken);
    }
  }
}

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.Modules.Notes.Application.Abstractions;
using Kakehashi.SharedKernel;

namespace Kakehashi.Modules.Notes.Application.Notes.Queries.GetNotes {
  /// <summary>Fetches the note list through the gateway.</summary>
  public sealed class GetNotesQueryHandler
      : IRequestHandler<GetNotesQuery, Result<IReadOnlyList<NoteDto>>> {
    private readonly INotesGateway _notes;

    public GetNotesQueryHandler(INotesGateway notes) {
      ArgumentNullException.ThrowIfNull(notes);
      _notes = notes;
    }

    // A pass-through, and it stays one until this query grows a reason not to be — sorting the
    // client cares about, a filter, a merge with something local. The handler is the place that
    // would live, which is why the view model talks to it rather than to the gateway.
    public Task<Result<IReadOnlyList<NoteDto>>> Handle(
        GetNotesQuery request, CancellationToken cancellationToken) {
      return _notes.ListAsync(cancellationToken);
    }
  }
}

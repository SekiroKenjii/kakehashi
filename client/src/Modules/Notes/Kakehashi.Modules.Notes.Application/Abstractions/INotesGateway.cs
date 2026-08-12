using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kakehashi.Modules.Notes.Application.Notes;
using Kakehashi.Modules.Notes.Domain.Notes;
using Kakehashi.SharedKernel;

namespace Kakehashi.Modules.Notes.Application.Abstractions {
  // Every method returns a Result rather than throwing: a backend that is down is an expected
  // state for a desktop app, not an exceptional one, so it belongs in the return type where a view
  // model has to deal with it. The write methods take a NoteDraft so nothing unvalidated can reach
  // the wire.
  public interface INotesGateway {
    // Ordered most recently updated first.
    Task<Result<IReadOnlyList<NoteDto>>> ListAsync(CancellationToken cancellationToken);

    // The returned note carries the server-assigned id.
    Task<Result<NoteDto>> CreateAsync(NoteDraft draft, CancellationToken cancellationToken);

    Task<Result<NoteDto>> UpdateAsync(
        long id, NoteDraft draft, CancellationToken cancellationToken);

    // Deleting a note that is already gone succeeds.
    Task<Result> DeleteAsync(long id, CancellationToken cancellationToken);
  }
}

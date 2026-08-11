using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kakehashi.Modules.Notes.Application.Notes;
using Kakehashi.Modules.Notes.Domain.Notes;
using Kakehashi.SharedKernel;

namespace Kakehashi.Modules.Notes.Application.Abstractions {
  // The notes module's port to the server. The application layer declares the contract; the
  // concrete adapter — which knows about gRPC, generated types and network failures — is supplied
  // by the UI layer at composition time.
  //
  // Every method returns a Result rather than throwing. A backend that is down is
  // an expected state for a desktop app, not an exceptional one: the user closed their laptop, or
  // the VPN dropped. It belongs in the return type, where a view model has to deal with it.
  //
  // The write methods take a NoteDraft, so nothing that has not been validated can
  // reach the wire. The type is the check.
  public interface INotesGateway {
    // Lists every note, most recently updated first.
    Task<Result<IReadOnlyList<NoteDto>>> ListAsync(CancellationToken cancellationToken);

    // Creates a note and returns it as stored, with its server-assigned id.
    Task<Result<NoteDto>> CreateAsync(NoteDraft draft, CancellationToken cancellationToken);

    // Rewrites a note's title and body.
    Task<Result<NoteDto>> UpdateAsync(
        long id, NoteDraft draft, CancellationToken cancellationToken);

    // Removes a note. Removing one that is already gone succeeds.
    Task<Result> DeleteAsync(long id, CancellationToken cancellationToken);
  }
}

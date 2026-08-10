using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kakehashi.Modules.Notes.Application.Notes;
using Kakehashi.Modules.Notes.Domain.Notes;
using Kakehashi.SharedKernel;

namespace Kakehashi.Modules.Notes.Application.Abstractions {
  /// <summary>
  /// The notes module's port to the server. The application layer declares the contract; the
  /// concrete adapter — which knows about gRPC, generated types and network failures — is supplied
  /// by the UI layer at composition time.
  /// </summary>
  /// <remarks>
  /// Every method returns a <see cref="Result"/> rather than throwing. A backend that is down is
  /// an expected state for a desktop app, not an exceptional one: the user closed their laptop, or
  /// the VPN dropped. It belongs in the return type, where a view model has to deal with it.
  /// <para>
  /// The write methods take a <see cref="NoteDraft"/>, so nothing that has not been validated can
  /// reach the wire. The type is the check.
  /// </para>
  /// </remarks>
  public interface INotesGateway {
    /// <summary>Lists every note, most recently updated first.</summary>
    Task<Result<IReadOnlyList<NoteDto>>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Creates a note and returns it as stored, with its server-assigned id.</summary>
    Task<Result<NoteDto>> CreateAsync(NoteDraft draft, CancellationToken cancellationToken);

    /// <summary>Rewrites a note's title and body.</summary>
    Task<Result<NoteDto>> UpdateAsync(
        long id, NoteDraft draft, CancellationToken cancellationToken);

    /// <summary>Removes a note. Removing one that is already gone succeeds.</summary>
    Task<Result> DeleteAsync(long id, CancellationToken cancellationToken);
  }
}

using System;
using System.Threading;
using System.Threading.Tasks;
using __ROOT_NAMESPACE__.Application.Abstractions.Messaging;
using __ROOT_NAMESPACE__.Modules.Notes.Application.Notes.Queries.GetNotes;
using __ROOT_NAMESPACE__.UI.Contracts;

namespace __ROOT_NAMESPACE__.Modules.Notes.UI;

/// <summary>
/// The Notes module's line on the Home page checklist: create one note, and watch it
/// cross both halves. It ticks itself once the server has a note to return.
/// </summary>
public sealed class NotesGettingStartedStep : IGettingStartedStep
{
    private readonly ISender _sender;

    public NotesGettingStartedStep(ISender sender)
    {
        ArgumentNullException.ThrowIfNull(sender);
        _sender = sender;
    }

    public string ModuleName => "Notes";

    public string Title => "Create a note in the Notes module";

    public string Subtitle =>
        "One round trip through everything: the page, the mediator, gRPC, the server module and "
            + "its table.";

    /// <summary>Done once a note exists. A server nothing can reach has none.</summary>
    public async Task<bool> IsDoneAsync(CancellationToken cancellationToken)
    {
        var notes = await _sender.Send(new GetNotesQuery(), cancellationToken).ConfigureAwait(false);

        return notes.IsSuccess && notes.Value.Count > 0;
    }
}

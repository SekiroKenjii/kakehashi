using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using __ROOT_NAMESPACE__.Application.Abstractions.Messaging;
using __ROOT_NAMESPACE__.Modules.Notes.Application;
using __ROOT_NAMESPACE__.Modules.Notes.Application.Abstractions;
using __ROOT_NAMESPACE__.Modules.Notes.Application.Notes;
using __ROOT_NAMESPACE__.Modules.Notes.Application.Notes.Commands.CreateNote;
using __ROOT_NAMESPACE__.Modules.Notes.Application.Notes.Commands.DeleteNote;
using __ROOT_NAMESPACE__.Modules.Notes.Application.Notes.Commands.UpdateNote;
using __ROOT_NAMESPACE__.Modules.Notes.Application.Notes.Queries.GetNotes;
using __ROOT_NAMESPACE__.Modules.Notes.Domain.Notes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace __ROOT_NAMESPACE__.Modules.Notes.IntegrationTests;

/// <summary>
/// Exercises the Notes module the way the host does: the real mediator, real handler discovery,
/// real validation — with the network replaced by an in-memory stand-in. The mediator is never
/// mocked; if handler registration breaks, these fail.
/// </summary>
public sealed class NotesModuleIntegrationTests
{
    private static ServiceProvider BuildProvider(INotesGateway gateway)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNotesApplication();
        services.AddSingleton(gateway);

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task CreateThenList_RoundTripsThroughTheMediator()
    {
        using var provider = BuildProvider(new InMemoryNotesGateway());
        var sender = provider.GetRequiredService<ISender>();

        var created = await sender.Send(new CreateNoteCommand("  Shopping list  ", "milk"));
        Assert.True(created.IsSuccess);
        Assert.Equal("Shopping list", created.Value.Title);

        var listed = await sender.Send(new GetNotesQuery());
        Assert.True(listed.IsSuccess);
        Assert.Equal("Shopping list", Assert.Single(listed.Value).Title);
    }

    [Fact]
    public async Task Create_BlankTitle_IsRejectedBeforeItReachesTheGateway()
    {
        var gateway = new InMemoryNotesGateway();
        using var provider = BuildProvider(gateway);
        var sender = provider.GetRequiredService<ISender>();

        var result = await sender.Send(new CreateNoteCommand("   ", "body"));

        Assert.True(result.IsFailure);
        Assert.Equal(NotesErrors.TitleRequired, result.Error);

        var listed = await sender.Send(new GetNotesQuery());
        Assert.Empty(listed.Value);
    }

    [Fact]
    public async Task Update_RewritesTheNoteAndMovesItToTheTop()
    {
        using var provider = BuildProvider(new InMemoryNotesGateway());
        var sender = provider.GetRequiredService<ISender>();

        var first = await sender.Send(new CreateNoteCommand("First", string.Empty));
        await sender.Send(new CreateNoteCommand("Second", string.Empty));

        var updated = await sender.Send(new UpdateNoteCommand(first.Value.Id, "First, edited", "new"));
        Assert.True(updated.IsSuccess);

        var listed = await sender.Send(new GetNotesQuery());
        // Newest-updated first, so the note just edited leads.
        Assert.Equal("First, edited", listed.Value[0].Title);
        Assert.Equal("new", listed.Value[0].Body);
    }

    [Fact]
    public async Task Delete_RemovesTheNoteAndSucceedsOnASecondAttempt()
    {
        using var provider = BuildProvider(new InMemoryNotesGateway());
        var sender = provider.GetRequiredService<ISender>();

        var created = await sender.Send(new CreateNoteCommand("Doomed", string.Empty));

        Assert.True((await sender.Send(new DeleteNoteCommand(created.Value.Id))).IsSuccess);
        Assert.Empty((await sender.Send(new GetNotesQuery())).Value);

        // The contract says a delete of something already gone succeeds, so a retry after a dropped
        // connection is safe.
        Assert.True((await sender.Send(new DeleteNoteCommand(created.Value.Id))).IsSuccess);
    }

    [Fact]
    public async Task GatewayFailure_SurfacesAsAFailedResultRatherThanAnException()
    {
        var gateway = new InMemoryNotesGateway { FailEverything = true };
        using var provider = BuildProvider(gateway);
        var sender = provider.GetRequiredService<ISender>();

        // A backend that is down is an ordinary state for a desktop app, not an exceptional one.
        var listed = await sender.Send(new GetNotesQuery());

        Assert.True(listed.IsFailure);
        Assert.Equal(NotesErrors.RequestFailed, listed.Error);
    }

    [Fact]
    public async Task EveryHandlerInTheModuleIsDiscovered()
    {
        // AddNotesApplication scans the assembly; a handler that was added but not found would only
        // fail at runtime, on the one screen that uses it.
        using var provider = BuildProvider(new InMemoryNotesGateway());
        var sender = provider.GetRequiredService<ISender>();

        var created = await sender.Send(new CreateNoteCommand("Title", string.Empty));
        IReadOnlyList<object?> answers = [
            created,
            await sender.Send(new UpdateNoteCommand(created.Value.Id, "Retitled", string.Empty)),
            await sender.Send(new GetNotesQuery()),
            await sender.Send(new DeleteNoteCommand(created.Value.Id)),
        ];

        Assert.All(answers, answer => Assert.NotNull(answer));
    }
}

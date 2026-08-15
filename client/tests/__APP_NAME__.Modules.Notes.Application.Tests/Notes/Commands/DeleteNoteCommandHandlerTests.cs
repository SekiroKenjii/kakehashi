using System.Threading;
using System.Threading.Tasks;
using __ROOT_NAMESPACE__.Modules.Notes.Application.Abstractions;
using __ROOT_NAMESPACE__.Modules.Notes.Application.Notes.Commands.DeleteNote;
using __ROOT_NAMESPACE__.Modules.Notes.Domain.Notes;
using __ROOT_NAMESPACE__.SharedKernel;
using NSubstitute;
using Xunit;

namespace __ROOT_NAMESPACE__.Modules.Notes.Application.Tests.Notes.Commands;

public sealed class DeleteNoteCommandHandlerTests
{
    private readonly INotesGateway _notes = Substitute.For<INotesGateway>();

    private DeleteNoteCommandHandler CreateHandler()
    {
        return new DeleteNoteCommandHandler(_notes);
    }

    [Fact]
    public async Task Handle_ForwardsTheIdToTheGateway()
    {
        _notes.DeleteAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var result = await CreateHandler().Handle(new DeleteNoteCommand(42), CancellationToken.None);

        Assert.True(result.IsSuccess);
        await _notes.Received(1).DeleteAsync(42, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_GatewayFailure_SurfacesTheError()
    {
        _notes.DeleteAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure(NotesErrors.RequestFailed));

        var result = await CreateHandler().Handle(new DeleteNoteCommand(42), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(NotesErrors.RequestFailed, result.Error);
    }
}

using System;
using System.Threading;
using System.Threading.Tasks;
using Kakehashi.Modules.Notes.Application.Abstractions;
using Kakehashi.Modules.Notes.Application.Notes;
using Kakehashi.Modules.Notes.Application.Notes.Commands.CreateNote;
using Kakehashi.Modules.Notes.Domain.Notes;
using Kakehashi.SharedKernel;
using NSubstitute;
using Xunit;

namespace Kakehashi.Modules.Notes.Application.Tests.Notes.Commands {
  public sealed class CreateNoteCommandHandlerTests {
    private readonly INotesGateway _notes = Substitute.For<INotesGateway>();

    private CreateNoteCommandHandler CreateHandler() {
      return new CreateNoteCommandHandler(_notes);
    }

    [Fact]
    public async Task Handle_ValidCommand_SendsTheTrimmedDraft() {
      _notes.CreateAsync(Arg.Any<NoteDraft>(), Arg.Any<CancellationToken>())
          .Returns(Result.Success(Sample("Groceries")));

      var result = await CreateHandler()
          .Handle(new CreateNoteCommand("  Groceries  ", "milk"), CancellationToken.None);

      Assert.True(result.IsSuccess);
      await _notes.Received(1).CreateAsync(
          Arg.Is<NoteDraft>(draft => draft != null && draft.Title == "Groceries" && draft.Body == "milk"),
          Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_BlankTitle_FailsWithoutCallingTheServer() {
      var result = await CreateHandler()
          .Handle(new CreateNoteCommand("   ", "body"), CancellationToken.None);

      Assert.True(result.IsFailure);
      Assert.Equal(NotesErrors.TitleRequired, result.Error);
      // Client-side validation exists to save this round trip, so a rejected draft must never
      // reach the gateway.
      await _notes.DidNotReceive().CreateAsync(
          Arg.Any<NoteDraft>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_GatewayFailure_SurfacesTheError() {
      _notes.CreateAsync(Arg.Any<NoteDraft>(), Arg.Any<CancellationToken>())
          .Returns(Result.Failure<NoteDto>(NotesErrors.RequestFailed));

      var result = await CreateHandler()
          .Handle(new CreateNoteCommand("Title", "body"), CancellationToken.None);

      Assert.True(result.IsFailure);
      Assert.Equal(NotesErrors.RequestFailed, result.Error);
    }

    private static NoteDto Sample(string title) {
      return new NoteDto(1, title, "milk", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
    }
  }
}

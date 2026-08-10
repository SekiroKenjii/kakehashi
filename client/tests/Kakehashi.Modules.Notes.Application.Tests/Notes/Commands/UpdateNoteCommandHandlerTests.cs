using System;
using System.Threading;
using System.Threading.Tasks;
using Kakehashi.Modules.Notes.Application.Abstractions;
using Kakehashi.Modules.Notes.Application.Notes;
using Kakehashi.Modules.Notes.Application.Notes.Commands.UpdateNote;
using Kakehashi.Modules.Notes.Domain.Notes;
using Kakehashi.SharedKernel;
using NSubstitute;
using Xunit;

namespace Kakehashi.Modules.Notes.Application.Tests.Notes.Commands {
  public sealed class UpdateNoteCommandHandlerTests {
    private readonly INotesGateway _notes = Substitute.For<INotesGateway>();

    private UpdateNoteCommandHandler CreateHandler() {
      return new UpdateNoteCommandHandler(_notes);
    }

    [Fact]
    public async Task Handle_ValidCommand_SendsTheIdAndTheTrimmedDraft() {
      _notes.UpdateAsync(Arg.Any<long>(), Arg.Any<NoteDraft>(), Arg.Any<CancellationToken>())
          .Returns(Result.Success(
              new NoteDto(7, "After", "new", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)));

      var result = await CreateHandler()
          .Handle(new UpdateNoteCommand(7, " After ", "new"), CancellationToken.None);

      Assert.True(result.IsSuccess);
      await _notes.Received(1).UpdateAsync(
          7,
          Arg.Is<NoteDraft>(draft => draft != null && draft.Title == "After"),
          Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_TitleTooLong_FailsWithoutCallingTheServer() {
      var command = new UpdateNoteCommand(
          7, new string('a', NoteDraft.MaxTitleLength + 1), "body");

      var result = await CreateHandler().Handle(command, CancellationToken.None);

      Assert.True(result.IsFailure);
      Assert.Equal(NotesErrors.TitleTooLong, result.Error);
      await _notes.DidNotReceive().UpdateAsync(
          Arg.Any<long>(), Arg.Any<NoteDraft>(), Arg.Any<CancellationToken>());
    }
  }
}

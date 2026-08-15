using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.Modules.Notes.Application.Notes;
using Kakehashi.Modules.Notes.Application.Notes.Commands.CreateNote;
using Kakehashi.Modules.Notes.Application.Notes.Commands.DeleteNote;
using Kakehashi.Modules.Notes.Application.Notes.Commands.UpdateNote;
using Kakehashi.Modules.Notes.Application.Notes.Queries.GetNotes;
using Kakehashi.Modules.Notes.Domain.Notes;
using Kakehashi.Modules.Notes.UI.ViewModels;
using Kakehashi.SharedKernel;
using NSubstitute;
using Xunit;

namespace Kakehashi.Modules.Notes.UI.Tests.ViewModels;

/// <summary>
/// Unit tests for <see cref="NotesViewModel"/>: loading, client-side paging, the edit dialog's
/// keep-open-on-error contract, and delete. The mediator is substituted; no XAML is constructed,
/// so nothing here needs a UI thread.
/// </summary>
public sealed class NotesViewModelTests
{
    private readonly ISender _sender = Substitute.For<ISender>();

    private static NoteDto Note(long id, string title, string body = "", int minutesAgo = 0)
    {
        var moment = DateTimeOffset.UtcNow.AddMinutes(-minutesAgo);

        return new NoteDto(id, title, body, moment, moment);
    }

    /// <summary>Stubs the notes query.</summary>
    /// <remarks>
    /// Matches "not null" rather than Arg.Any. ISender.Send is generic, so every setup here
    /// configures the same method and they are told apart only by the argument matcher — and
    /// Arg.Any&lt;T&gt;() also matches the null a Received/DidNotReceive verification passes in.
    /// The symptom is a request of one type answered with another type's Task, cast-failing deep
    /// inside the proxy.
    /// </remarks>
    private void GivenNotes(params NoteDto[] notes)
    {
        _sender.Send(Arg.Is<GetNotesQuery>(query => query != null))
            .Returns(_ => Task.FromResult(
                Result.Success<IReadOnlyList<NoteDto>>(notes)));
    }

    private NotesViewModel CreateViewModel()
    {
        return new NotesViewModel(_sender);
    }

    [Fact]
    public async Task Load_PopulatesTheList()
    {
        GivenNotes(Note(1, "First", "some body"), Note(2, "Second"));
        var viewModel = CreateViewModel();

        await viewModel.LoadCommand.ExecuteAsync(parameter: null);

        Assert.True(viewModel.HasNotes);
        Assert.False(viewModel.HasNoNotes);
        Assert.Equal(2, viewModel.Notes.Count);
        Assert.Equal("First", viewModel.Notes[0].Title);
    }

    [Fact]
    public async Task Load_EmptyResult_ShowsTheEmptyState()
    {
        GivenNotes();
        var viewModel = CreateViewModel();

        await viewModel.LoadCommand.ExecuteAsync(parameter: null);

        Assert.False(viewModel.HasNotes);
        Assert.True(viewModel.HasNoNotes);
        Assert.Empty(viewModel.Notes);
    }

    [Fact]
    public async Task Load_Failure_ShowsTheErrorAndKeepsWhatWasOnScreen()
    {
        GivenNotes(Note(1, "Keep me"));
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.ExecuteAsync(parameter: null);

        _sender.Send(Arg.Is<GetNotesQuery>(query => query != null))
            .Returns(_ => Task.FromResult(
                Result.Failure<IReadOnlyList<NoteDto>>(NotesErrors.RequestFailed)));
        await viewModel.LoadCommand.ExecuteAsync(parameter: null);

        Assert.True(viewModel.HasError);
        Assert.Equal(NotesErrors.RequestFailed.Message, viewModel.ErrorMessage);
        // A backend that blinked must not also wipe the list the user was reading.
        Assert.Equal("Keep me", Assert.Single(viewModel.Notes).Title);
    }

    [Fact]
    public async Task Load_BodyWithNewlines_CollapsesToASingleLinePreview()
    {
        GivenNotes(Note(1, "Title", "first line\n\nsecond   line"));
        var viewModel = CreateViewModel();

        await viewModel.LoadCommand.ExecuteAsync(parameter: null);

        // Honouring newlines would make every row a different height and the list would jump.
        Assert.Equal("first line second line", viewModel.Notes[0].Preview);
        Assert.True(viewModel.Notes[0].HasPreview);
    }

    [Fact]
    public async Task Load_EmptyBody_HasNoPreview()
    {
        GivenNotes(Note(1, "Title", "   "));
        var viewModel = CreateViewModel();

        await viewModel.LoadCommand.ExecuteAsync(parameter: null);

        Assert.False(viewModel.Notes[0].HasPreview);
        Assert.True(viewModel.Notes[0].HasNoPreview);
    }

    [Fact]
    public async Task Load_SinglePage_HidesThePager()
    {
        GivenNotes([.. Enumerable
            .Range(1, 5)
            .Select(i => Note(i, $"Note {i}"))]);
        var viewModel = CreateViewModel();

        await viewModel.LoadCommand.ExecuteAsync(parameter: null);

        Assert.False(viewModel.HasPaging);
        Assert.Equal(5, viewModel.Notes.Count);
    }

    [Fact]
    public async Task Load_MoreThanOnePage_PagesFiveAtATime()
    {
        GivenNotes([.. Enumerable
            .Range(1, 12)
            .Select(i => Note(i, $"Note {i}"))]);
        var viewModel = CreateViewModel();

        await viewModel.LoadCommand.ExecuteAsync(parameter: null);

        Assert.True(viewModel.HasPaging);
        Assert.Equal(5, viewModel.Notes.Count);
        Assert.Equal("1 / 3", viewModel.PageLabel);

        viewModel.NextPageCommand.Execute(parameter: null);
        Assert.Equal("2 / 3", viewModel.PageLabel);
        Assert.Equal("Note 6", viewModel.Notes[0].Title);

        viewModel.NextPageCommand.Execute(parameter: null);
        Assert.Equal("3 / 3", viewModel.PageLabel);
        Assert.Equal(2, viewModel.Notes.Count);
    }

    [Fact]
    public async Task NextPage_AtTheEnd_StaysPut()
    {
        GivenNotes([.. Enumerable
            .Range(1, 7)
            .Select(i => Note(i, $"Note {i}"))]);
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.ExecuteAsync(parameter: null);

        viewModel.NextPageCommand.Execute(parameter: null);
        viewModel.NextPageCommand.Execute(parameter: null);
        viewModel.PrevPageCommand.Execute(parameter: null);
        viewModel.PrevPageCommand.Execute(parameter: null);
        viewModel.PrevPageCommand.Execute(parameter: null);

        Assert.Equal("1 / 2", viewModel.PageLabel);
    }

    [Fact]
    public async Task Save_NewNote_SendsACreateAndReloads()
    {
        GivenNotes();
        _sender.Send(Arg.Is<CreateNoteCommand>(command => command != null))
            .Returns(_ => Task.FromResult(Result.Success(Note(1, "Written"))));
        var viewModel = CreateViewModel();

        viewModel.PrepareCreate();
        viewModel.EditTitle = "Written";
        viewModel.EditBody = "body";
        var saved = await viewModel.SaveAsync();

        Assert.True(saved);
        Assert.Null(viewModel.DialogError);
        await _sender.Received(1).Send(
            Arg.Is<CreateNoteCommand>(c => c != null && c.Title == "Written" && c.Body == "body"));
    }

    [Fact]
    public async Task Save_Rejected_KeepsTheDialogOpenWithTheServersMessage()
    {
        GivenNotes();
        _sender.Send(Arg.Is<CreateNoteCommand>(command => command != null))
            .Returns(_ => Task.FromResult(Result.Failure<NoteDto>(NotesErrors.TitleRequired)));
        var viewModel = CreateViewModel();

        viewModel.PrepareCreate();
        var saved = await viewModel.SaveAsync();

        // False keeps the ContentDialog open, so a rejected title does not cost the user the body
        // they just typed.
        Assert.False(saved);
        Assert.True(viewModel.HasDialogError);
        Assert.Equal(NotesErrors.TitleRequired.Message, viewModel.DialogError);
    }

    [Fact]
    public async Task Save_AfterPrepareEdit_SendsAnUpdateForThatNote()
    {
        GivenNotes(Note(7, "Before", "old"));
        _sender.Send(Arg.Is<UpdateNoteCommand>(command => command != null))
            .Returns(_ => Task.FromResult(Result.Success(Note(7, "After", "new"))));
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.ExecuteAsync(parameter: null);

        await viewModel.PrepareEditAsync(viewModel.Notes[0]);
        Assert.Equal("Before", viewModel.EditTitle);
        // The row only carries a preview, so the dialog refetches the full body.
        Assert.Equal("old", viewModel.EditBody);

        viewModel.EditTitle = "After";
        var saved = await viewModel.SaveAsync();

        Assert.True(saved);
        await _sender.Received(1).Send(Arg.Is<UpdateNoteCommand>(c => c != null && c.Id == 7));
    }

    [Fact]
    public async Task ConfirmDelete_SendsADeleteForThePreparedRow()
    {
        GivenNotes(Note(3, "Doomed"));
        _sender.Send(Arg.Is<DeleteNoteCommand>(command => command != null))
            .Returns(_ => Task.FromResult(Result.Success()));
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.ExecuteAsync(parameter: null);

        viewModel.PrepareDelete(viewModel.Notes[0]);
        Assert.Contains("Doomed", viewModel.DeletePrompt, StringComparison.Ordinal);

        var deleted = await viewModel.ConfirmDeleteAsync();

        Assert.True(deleted);
        await _sender.Received(1).Send(Arg.Is<DeleteNoteCommand>(c => c != null && c.Id == 3));
    }

    [Fact]
    public async Task ConfirmDelete_WithoutAPreparedRow_DoesNothing()
    {
        GivenNotes();
        var viewModel = CreateViewModel();

        var deleted = await viewModel.ConfirmDeleteAsync();

        Assert.False(deleted);
        await _sender.DidNotReceive().Send(Arg.Any<DeleteNoteCommand>());
    }

    [Fact]
    public async Task ConfirmDelete_Failure_ReportsOnThePageNotTheDialog()
    {
        GivenNotes(Note(3, "Doomed"));
        _sender.Send(Arg.Is<DeleteNoteCommand>(command => command != null))
            .Returns(_ => Task.FromResult(Result.Failure(NotesErrors.RequestFailed)));
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.ExecuteAsync(parameter: null);

        viewModel.PrepareDelete(viewModel.Notes[0]);
        var deleted = await viewModel.ConfirmDeleteAsync();

        Assert.False(deleted);
        Assert.True(viewModel.HasError);
        Assert.Equal(NotesErrors.RequestFailed.Message, viewModel.ErrorMessage);
    }
}

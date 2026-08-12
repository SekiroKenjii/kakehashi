using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.Modules.Notes.Application.Notes;
using Kakehashi.Modules.Notes.Application.Notes.Commands.CreateNote;
using Kakehashi.Modules.Notes.Application.Notes.Commands.DeleteNote;
using Kakehashi.Modules.Notes.Application.Notes.Commands.UpdateNote;
using Kakehashi.Modules.Notes.Application.Notes.Queries.GetNotes;
using Kakehashi.UI.Contracts;

namespace Kakehashi.Modules.Notes.UI.ViewModels {
  public sealed record NoteListItem(long Id, string Title, string Preview, string TimeText) {
    public bool HasPreview => Preview.Length > 0;

    public bool HasNoPreview => !HasPreview;
  }

  public sealed partial class NotesViewModel : ViewModel {
    private const int _pageSize = 5;
    private const int _previewLength = 90;

    private readonly ISender _sender;
    private List<NoteListItem> _allNotes = [];
    private int _page = 1;
    private long? _editingId;
    private NoteListItem? _pendingDelete;

    public NotesViewModel(ISender sender) {
      ArgumentNullException.ThrowIfNull(sender);
      _sender = sender;
      PageLabel = string.Empty;
      DialogHeader = string.Empty;
      DeletePrompt = string.Empty;
      EditTitle = string.Empty;
      EditBody = string.Empty;
    }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoNotes))]
    public partial bool HasNotes { get; set; }

    [ObservableProperty]
    public partial bool HasPaging { get; set; }

    [ObservableProperty]
    public partial string PageLabel { get; set; }

    [ObservableProperty]
    public partial string DialogHeader { get; set; }

    [ObservableProperty]
    public partial string EditTitle { get; set; }

    [ObservableProperty]
    public partial string EditBody { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDialogError))]
    public partial string? DialogError { get; set; }

    [ObservableProperty]
    public partial string DeletePrompt { get; set; }

    // The current page only, at most _pageSize rows; _allNotes holds the rest.
    public ObservableCollection<NoteListItem> Notes { get; } = [];

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public bool HasNoNotes => !HasNotes;

    public bool HasDialogError => !string.IsNullOrEmpty(DialogError);

    [RelayCommand]
    private async Task LoadAsync() {
      if (IsBusy) {
        return;
      }
      IsBusy = true;
      try {
        ErrorMessage = null;

        var result = await _sender.Send(new GetNotesQuery());
        if (result.IsFailure) {
          ErrorMessage = result.Error.Message;
          // Leave what is on screen alone: a backend that blinked should not also wipe the list
          // the user was reading.
          return;
        }

        _allNotes = [.. result.Value.Select(ToListItem)];
        HasNotes = _allNotes.Count > 0;
        ShowPage(_page);
      } finally {
        IsBusy = false;
      }
    }

    [RelayCommand]
    private void PrevPage() {
      ShowPage(_page - 1);
    }

    [RelayCommand]
    private void NextPage() {
      ShowPage(_page + 1);
    }

    public void PrepareCreate() {
      _editingId = null;
      DialogHeader = "New note";
      DialogError = null;
      EditTitle = string.Empty;
      EditBody = string.Empty;
    }

    public async Task PrepareEditAsync(NoteListItem item) {
      ArgumentNullException.ThrowIfNull(item);

      _editingId = item.Id;
      DialogHeader = "Edit note";
      DialogError = null;
      EditTitle = item.Title;
      EditBody = string.Empty;

      // The row carries only a preview, so the body is refetched — which also catches a change
      // made on another device.
      var result = await _sender.Send(new GetNotesQuery());
      if (result.IsSuccess) {
        var current = result.Value.FirstOrDefault(note => note.Id == item.Id);
        if (current is not null) {
          EditTitle = current.Title;
          EditBody = current.Body;
        }
      }
    }

    // Returns false, after setting DialogError, to keep the dialog open so a rejected title does
    // not cost the user what they typed.
    public async Task<bool> SaveAsync() {
      DialogError = null;

      var result = _editingId is { } id
          ? await _sender.Send(new UpdateNoteCommand(id, EditTitle, EditBody))
          : await _sender.Send(new CreateNoteCommand(EditTitle, EditBody));

      if (result.IsFailure) {
        DialogError = result.Error.Message;
        return false;
      }

      // A new note sorts to the top; jump there so the user sees what they just made.
      if (_editingId is null) {
        _page = 1;
      }
      await LoadAsync();
      return true;
    }

    public void PrepareDelete(NoteListItem item) {
      ArgumentNullException.ThrowIfNull(item);
      _pendingDelete = item;
      DeletePrompt = $"Delete “{item.Title}”? This cannot be undone.";
    }

    public async Task<bool> ConfirmDeleteAsync() {
      if (_pendingDelete is not { } item) {
        return false;
      }
      _pendingDelete = null;

      var result = await _sender.Send(new DeleteNoteCommand(item.Id));
      if (result.IsFailure) {
        ErrorMessage = result.Error.Message;
        return false;
      }

      await LoadAsync();
      return true;
    }

    private static NoteListItem ToListItem(NoteDto note) {
      return new NoteListItem(
          note.Id, note.Title, BuildPreview(note.Body), FormatRelative(note.UpdatedAt));
    }

    private static string BuildPreview(string body) {
      // Honouring newlines would make every row a different height and the list would jump around
      // as it pages.
      var line = string.Join(' ', body.Split(
          (char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
      if (line.Length == 0) {
        return string.Empty;
      }

      var elements = new StringInfo(line);
      return elements.LengthInTextElements <= _previewLength
          ? line
          : elements.SubstringByTextElements(0, _previewLength) + "…";
    }

    private static string FormatRelative(DateTimeOffset moment) {
      var elapsed = DateTimeOffset.UtcNow - moment;

      if (elapsed < TimeSpan.FromMinutes(1)) {
        return "now";
      }
      if (elapsed < TimeSpan.FromHours(1)) {
        return $"{(int)elapsed.TotalMinutes}m ago";
      }
      if (elapsed < TimeSpan.FromDays(1)) {
        return $"{(int)elapsed.TotalHours}h ago";
      }
      if (elapsed < TimeSpan.FromDays(7)) {
        return $"{(int)elapsed.TotalDays}d ago";
      }
      return moment.ToLocalTime().ToString("MMM d, yyyy", CultureInfo.CurrentCulture);
    }

    private void ShowPage(int page) {
      var pageCount = Math.Max(1, (int)Math.Ceiling(_allNotes.Count / (double)_pageSize));
      _page = Math.Clamp(page, 1, pageCount);

      Notes.Clear();
      foreach (var item in _allNotes.Skip((_page - 1) * _pageSize).Take(_pageSize)) {
        Notes.Add(item);
      }

      HasPaging = pageCount > 1;
      PageLabel = $"{_page} / {pageCount}";
    }
  }
}

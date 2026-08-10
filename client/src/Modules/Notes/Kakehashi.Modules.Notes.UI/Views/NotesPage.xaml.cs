using System;
using Kakehashi.Modules.Notes.UI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kakehashi.Modules.Notes.UI.Views {
  /// <summary>
  /// The notes page: the note list served by the backend, with create, edit and delete through
  /// dialogs.
  /// </summary>
  public sealed partial class NotesPage : Page {
    public NotesPage(NotesViewModel viewModel) {
      ArgumentNullException.ThrowIfNull(viewModel);
      ViewModel = viewModel;

      InitializeComponent();

      Loaded += OnLoaded;
    }

    public NotesViewModel ViewModel { get; }

    private async void OnLoaded(object sender, RoutedEventArgs e) {
      await ViewModel.LoadCommand.ExecuteAsync(parameter: null);
    }

    private async void OnNewNote(object sender, RoutedEventArgs e) {
      ViewModel.PrepareCreate();
      await EditNoteDialog.ShowAsync();
    }

    private async void OnEditNote(object sender, RoutedEventArgs e) {
      if ((sender as FrameworkElement)?.Tag is NoteListItem item) {
        await ViewModel.PrepareEditAsync(item);
        await EditNoteDialog.ShowAsync();
      }
    }

    private async void OnDeleteNote(object sender, RoutedEventArgs e) {
      if ((sender as FrameworkElement)?.Tag is NoteListItem item) {
        ViewModel.PrepareDelete(item);
        await DeleteNoteDialog.ShowAsync();
      }
    }

    // Cancelling the close keeps the dialog open so the validation error stays visible — and so a
    // rejected title does not cost the user the body they just typed.
    private async void OnSaveNote(ContentDialog sender, ContentDialogButtonClickEventArgs args) {
      var deferral = args.GetDeferral();
      try {
        args.Cancel = !await ViewModel.SaveAsync();
      } finally {
        deferral.Complete();
      }
    }

    private async void OnConfirmDelete(
        ContentDialog sender, ContentDialogButtonClickEventArgs args) {
      var deferral = args.GetDeferral();
      try {
        args.Cancel = !await ViewModel.ConfirmDeleteAsync();
      } finally {
        deferral.Complete();
      }
    }
  }
}

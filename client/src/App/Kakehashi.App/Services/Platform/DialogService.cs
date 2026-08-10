using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Kakehashi.UI.Contracts.Services.Platform;
using Microsoft.UI.Xaml.Controls;

namespace Kakehashi.App.Services.Platform {
  /// <summary>Shows modal <see cref="ContentDialog"/>s anchored to the main window's XAML root.</summary>
  public sealed class DialogService : IDialogService {
    public async Task ShowMessageAsync(string title, string message, string closeText = "OK") {
      var dialog = new ContentDialog {
        Title = title,
        Content = message,
        CloseButtonText = closeText,
        XamlRoot = App.MainWindow.Content.XamlRoot,
      };

      await dialog.ShowAsync();
    }

    public async Task<bool> ShowConfirmAsync(
        string title, string message, string primaryText = "Yes", string closeText = "No") {
      var dialog = new ContentDialog {
        Title = title,
        Content = message,
        PrimaryButtonText = primaryText,
        CloseButtonText = closeText,
        DefaultButton = ContentDialogButton.Primary,
        XamlRoot = App.MainWindow.Content.XamlRoot,
      };

      return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    public async Task<string?> ShowPromptAsync(
        string title, string label, string initialValue = "", string primaryText = "OK") {
      var input = new TextBox { Header = label, Text = initialValue, AcceptsReturn = false };
      var dialog = new ContentDialog {
        Title = title,
        Content = input,
        PrimaryButtonText = primaryText,
        CloseButtonText = "Cancel",
        DefaultButton = ContentDialogButton.Primary,
        XamlRoot = App.MainWindow.Content.XamlRoot,
      };

      return await dialog.ShowAsync() == ContentDialogResult.Primary ? input.Text : null;
    }

    public async Task<IReadOnlyList<string>?> ShowInputsAsync(
        string title,
        string primaryText,
        params (string Label, string InitialValue, bool IsSecret)[] fields) {
      var panel = new StackPanel { Spacing = 12, MinWidth = 360 };
      var inputs = new List<Control>(fields.Length);
      foreach (var (label, initialValue, isSecret) in fields) {
        // A PasswordBox for a secret, and not merely for the dots. It also keeps the value out of
        // the clipboard history and off the screen of whoever is standing behind an administrator
        // creating an account — which is precisely when these two dialogs are used.
        Control input = isSecret
            ? new PasswordBox { Header = label, Password = initialValue }
            : new TextBox { Header = label, Text = initialValue, AcceptsReturn = false };
        inputs.Add(input);
        panel.Children.Add(input);
      }

      var dialog = new ContentDialog {
        Title = title,
        Content = panel,
        PrimaryButtonText = primaryText,
        CloseButtonText = "Cancel",
        DefaultButton = ContentDialogButton.Primary,
        XamlRoot = App.MainWindow.Content.XamlRoot,
      };

      if (await dialog.ShowAsync() != ContentDialogResult.Primary) {
        return null;
      }

      var values = new List<string>(inputs.Count);
      foreach (var input in inputs) {
        values.Add(input switch {
          PasswordBox password => password.Password,
          TextBox text => text.Text,
          _ => string.Empty,
        });
      }
      return values;
    }
  }
}

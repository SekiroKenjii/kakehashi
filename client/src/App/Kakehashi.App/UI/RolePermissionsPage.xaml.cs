using System;
using Kakehashi.App.Services;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Kakehashi.App.UI {
  // Loads on FrameworkElement.Loaded, not OnNavigatedTo: the navigation service sets Frame.Content
  // directly — pages come from the container, not from Frame.Navigate — so the navigation overrides
  // never fire. Every page in this app loads the same way.
  //
  // The static helpers exist because x:Bind calls functions but does not do arithmetic or string
  // formatting. They are on the page rather than in converters for the reason the rest of this
  // codebase prefers: a function is compile-checked against its arguments, a converter is not.
  public sealed partial class RolePermissionsPage : Page {
    public RolePermissionsPage(RolePermissionsViewModel viewModel) {
      ArgumentNullException.ThrowIfNull(viewModel);
      ViewModel = viewModel;

      InitializeComponent();
      Loaded += OnLoaded;
    }

    public RolePermissionsViewModel ViewModel { get; }

    public static string DescribePerms(int permissionCount, int permissionTotal) {
      return $"{permissionCount}/{permissionTotal} perms";
    }

    public static string DescribeUsers(int accountCount) {
      return accountCount == 1 ? "1 user" : $"{accountCount} users";
    }

    // The description is deliberately absent: it is already on the role card two inches to the
    // left, and including it here pushed the line past the header's width, so the counts — the
    // part that is only here — were the half that got trimmed away.
    public static string DescribeRoleSub(RoleRow? role) {
      if (role is null) {
        return string.Empty;
      }
      var users = role.AccountCount == 1 ? "1 user" : $"{role.AccountCount} users";
      return $"{role.PermissionCount}/{role.PermissionTotal} permissions · assigned to {users}";
    }

    public static string NameOf(RoleRow? role) {
      return role?.Name ?? string.Empty;
    }

    public static string InitialsOf(RoleRow? role) {
      return role is null ? string.Empty : AdminFormat.Initials(role.Name);
    }

    public static string DescribeChanges(int count) {
      return count == 1 ? "1 unsaved change" : $"{count} unsaved changes";
    }

    public static string GroupGlyph(string category) {
      return category switch {
        "Administration" => "",
        "Module access" => "",
        _ => "",
      };
    }

    public static string DescribeAudit(string action, string roleName, string permissionKey) {
      return permissionKey.Length == 0
          ? $"{action} · {roleName}"
          : $"{action} · {roleName} · {permissionKey}";
    }

    public static string DescribeActor(string actorName, DateTimeOffset at, string detail) {
      var line = $"{actorName} · {AdminFormat.Relative(at)}";
      return detail.Length == 0 ? line : $"{line} · {detail}";
    }

    public static SolidColorBrush ChangedBackground(bool isChanged) {
      return isChanged ? _changedBackground : _transparent;
    }

    public static SolidColorBrush ChangedBorder(bool isChanged) {
      return isChanged ? _changedBorder : _transparent;
    }

    // x:Bind converts a bool to Visibility on its own but has no operator for "not", and a second
    // property on the view model for every negated one is worse than one function here.
    public static Visibility Not(bool value) {
      return value ? Visibility.Collapsed : Visibility.Visible;
    }

    private static readonly SolidColorBrush _transparent = new(Colors.Transparent);
    private static readonly SolidColorBrush _changedBorder =
        new(Color.FromArgb(0xFF, 0xCA, 0x50, 0x10));
    private static readonly SolidColorBrush _changedBackground =
        new(Color.FromArgb(0x14, 0xCA, 0x50, 0x10));

    private void OnLoaded(object sender, RoutedEventArgs e) {
      FilterBar.SelectedItem = ChipAll;
      _ = ViewModel.LoadCommand.ExecuteAsync(parameter: null);
    }

    private void OnFilterChipChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args) {
      if (sender.SelectedItem?.Text is { } text) {
        ViewModel.StateFilter = text;
      }
    }
  }
}

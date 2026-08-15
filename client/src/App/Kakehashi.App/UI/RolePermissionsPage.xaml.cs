using System;
using Kakehashi.App.Services;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Kakehashi.App.UI;

/// <summary>
/// The Role Permissions screen.
/// </summary>
/// <remarks>client/docs/architecture.md, "Static helpers on the page, not converters".</remarks>
public sealed partial class RolePermissionsPage : Page
{
    public RolePermissionsPage(RolePermissionsViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ViewModel = viewModel;

        InitializeComponent();
        Loaded += OnLoaded;
    }

    public RolePermissionsViewModel ViewModel { get; }

    /// <summary>"22/34 perms" on a role card.</summary>
    public static string DescribePerms(int permissionCount, int permissionTotal)
    {
        return $"{permissionCount}/{permissionTotal} perms";
    }

    public static string DescribeUsers(int accountCount)
    {
        return accountCount == 1 ? "1 user" : $"{accountCount} users";
    }

    /// <summary>
    /// The matrix header's one-line summary of the selected role.
    /// </summary>
    /// <remarks>
    /// The description is deliberately absent: it is already on the role card two inches to the
    /// left, and including it here pushed the line past the header's width, so the counts — the
    /// part that is only here — were the half that got trimmed away.
    /// </remarks>
    public static string DescribeRoleSub(RoleRow? role)
    {
        if (role is null)
        {
            return string.Empty;
        }
        var users = role.AccountCount == 1 ? "1 user" : $"{role.AccountCount} users";
        return $"{role.PermissionCount}/{role.PermissionTotal} permissions · assigned to {users}";
    }

    public static string NameOf(RoleRow? role)
    {
        return role?.Name ?? string.Empty;
    }

    public static string InitialsOf(RoleRow? role)
    {
        return role is null ? string.Empty : AdminFormat.Initials(role.Name);
    }

    /// <summary>The unsaved-changes bar's headline.</summary>
    public static string DescribeChanges(int count)
    {
        return count == 1 ? "1 unsaved change" : $"{count} unsaved changes";
    }

    /// <summary>The icon on a category header.</summary>
    public static string GroupGlyph(string category)
    {
        return category switch {
            "Administration" => "",
            "Module access" => "",
            _ => "",
        };
    }

    /// <summary>The headline of one audit entry.</summary>
    public static string DescribeAudit(string action, string roleName, string permissionKey)
    {
        return permissionKey.Length == 0
            ? $"{action} · {roleName}"
            : $"{action} · {roleName} · {permissionKey}";
    }

    /// <summary>Who did it and when, plus whatever the entry carried.</summary>
    public static string DescribeActor(string actorName, DateTimeOffset at, string detail)
    {
        var line = $"{actorName} · {AdminFormat.Relative(at)}";
        return detail.Length == 0 ? line : $"{line} · {detail}";
    }

    /// <summary>The changed-row tint: a caution wash behind a staged row, nothing otherwise.</summary>
    public static SolidColorBrush ChangedBackground(bool isChanged)
    {
        return isChanged ? _changedBackground : _transparent;
    }

    /// <summary>The 3px caution edge on a staged row — the mockup's changed marker.</summary>
    public static SolidColorBrush ChangedBorder(bool isChanged)
    {
        return isChanged ? _changedBorder : _transparent;
    }

    /// <summary>
    /// The inverse of a flag, as a visibility.
    /// </summary>
    /// <remarks>
    /// x:Bind converts a bool to Visibility on its own but has no operator for "not", and a
    /// second property on the view model for every negated one is worse than one function here.
    /// </remarks>
    public static Visibility Not(bool value)
    {
        return value ? Visibility.Collapsed : Visibility.Visible;
    }

    private static readonly SolidColorBrush _transparent = new(Colors.Transparent);
    private static readonly SolidColorBrush _changedBorder =
        new(Color.FromArgb(0xFF, 0xCA, 0x50, 0x10));
    private static readonly SolidColorBrush _changedBackground =
        new(Color.FromArgb(0x14, 0xCA, 0x50, 0x10));

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        FilterBar.SelectedItem = ChipAll;
        _ = ViewModel.LoadCommand.ExecuteAsync(parameter: null);
    }

    private void OnFilterChipChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (sender.SelectedItem?.Text is { } text)
        {
            ViewModel.StateFilter = text;
        }
    }
}

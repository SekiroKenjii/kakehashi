using System;
using System.Collections.Generic;
using Kakehashi.App.Services;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Kakehashi.App.UI;

/// <summary>
/// The Users screen.
/// </summary>
/// <remarks>client/docs/architecture.md, "Static helpers on the page, not converters".</remarks>
public sealed partial class UsersPage : Page
{
    public UsersPage(UsersViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ViewModel = viewModel;

        InitializeComponent();
        Loaded += OnLoaded;
    }

    public UsersViewModel ViewModel { get; }

    /// <summary>"Admin, Developer", or a dash for somebody holding none.</summary>
    public static string DescribeRoles(IReadOnlyList<string> roleNames)
    {
        return roleNames.Count == 0 ? "—" : string.Join(", ", roleNames);
    }

    /// <summary>The first role's name, for the coloured badge. Empty hides the badge.</summary>
    public static string FirstRole(IReadOnlyList<string> roleNames)
    {
        return roleNames.Count == 0 ? string.Empty : roleNames[0];
    }

    /// <summary>The badge's foreground. Flat because x:Bind cannot nest function calls.</summary>
    public static SolidColorBrush FirstRoleForeground(IReadOnlyList<string> roleNames)
    {
        return AdminFormat.RoleForeground(FirstRole(roleNames));
    }

    public static SolidColorBrush FirstRoleBackground(IReadOnlyList<string> roleNames)
    {
        return AdminFormat.RoleBackground(FirstRole(roleNames));
    }

    /// <summary>"+2" when the user holds more roles than the one badge shows.</summary>
    public static string MoreRoles(IReadOnlyList<string> roleNames)
    {
        return roleNames.Count > 1 ? $"+{roleNames.Count - 1}" : string.Empty;
    }

    public static Visibility HasMoreRoles(IReadOnlyList<string> roleNames)
    {
        return roleNames.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
    }

    public static Visibility HasAnyRole(IReadOnlyList<string> roleNames)
    {
        return roleNames.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>"Active", "Inactive", or "Never signed in" — the status column's text.</summary>
    public static string DescribeStatus(bool isActive, DateTimeOffset? lastSignIn)
    {
        if (!isActive)
        {
            return "Inactive";
        }
        return lastSignIn is null ? "Never signed in" : "Active";
    }

    /// <summary>The status dot and its label: green for an account that signs in, grey otherwise.</summary>
    public static SolidColorBrush StatusBrush(bool isActive, DateTimeOffset? lastSignIn)
    {
        return isActive && lastSignIn is not null ? _statusActive : _statusInactive;
    }

    /// <summary>The selected user's status, for the badge under their name.</summary>
    public static string SelectedStatus(UserRow? user)
    {
        return user is null ? string.Empty : DescribeStatus(user.IsActive, user.LastSignInAt);
    }

    public static SolidColorBrush SelectedStatusBrush(UserRow? user)
    {
        return user is null ? _statusInactive : StatusBrush(user.IsActive, user.LastSignInAt);
    }

    /// <summary>Whether the Remove button on an assigned role is shown at all.</summary>
    public static Visibility WhenTrue(bool value)
    {
        return value ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>"2 min ago" in the last-login column; a dash for never.</summary>
    public static string DescribeLastSignIn(DateTimeOffset? at)
    {
        return at is null ? "—" : AdminFormat.Relative(at.Value);
    }

    public static string DescribeCreated(DateTimeOffset at)
    {
        return at.ToString("yyyy-MM-dd");
    }

    public static string DescribeSelection(UserRow? user)
    {
        return user is null ? string.Empty : user.DisplayName;
    }

    public static string Email(UserRow? user)
    {
        return user?.Email ?? string.Empty;
    }

    public static string InitialsOf(UserRow? user)
    {
        return user is null ? string.Empty : AdminFormat.Initials(user.DisplayName);
    }

    public static string CreatedLine(UserRow? user)
    {
        return user is null ? string.Empty : $"{user.CreatedAt:yyyy-MM-dd} · {Age(user.CreatedAt)}";
    }

    public static string SessionsHeader(int count)
    {
        return $"ACTIVE SESSIONS ({count})";
    }

    /// <summary>The device the session claimed, or the client when it claimed nothing.</summary>
    public static string SessionTitle(string device, string client)
    {
        return device.Length == 0 ? client : device;
    }

    public static string SessionMeta(string ipAddress, DateTimeOffset lastSeenAt)
    {
        return $"{ipAddress} · last seen {AdminFormat.Relative(lastSeenAt)}";
    }

    /// <summary>The danger-zone row's title. The button beside it says just the verb.</summary>
    public static string DescribeToggleTitle(UserRow? user)
    {
        return user is { IsActive: true } ? "Deactivate account" : "Reactivate account";
    }

    public static string DescribeToggle(UserRow? user)
    {
        return user is { IsActive: true } ? "Deactivate" : "Reactivate";
    }

    public static string DescribeToggleSub(UserRow? user)
    {
        return user is { IsActive: true }
            ? "User cannot sign in until reactivated"
            : "Restores the account's ability to sign in";
    }

    /// <summary>A dash where a value is optional and absent, so the row still reads as a row.</summary>
    public static string OrDash(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "—" : value;
    }

    public static Visibility WhenEmpty(int count)
    {
        return count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public static Visibility WhenText(string value)
    {
        return string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>The add-a-role row appears only when there is a role to add and the right to add it.</summary>
    public static Visibility WhenAssignable(bool canManageRoles, int assignableCount)
    {
        return canManageRoles && assignableCount > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public static Visibility Not(bool value)
    {
        return value ? Visibility.Collapsed : Visibility.Visible;
    }

    private static readonly SolidColorBrush _statusActive =
        new(Color.FromArgb(0xFF, 0x2E, 0x9E, 0x44));
    private static readonly SolidColorBrush _statusInactive =
        new(Colors.Gray);

    private static string Age(DateTimeOffset created)
    {
        var age = DateTimeOffset.Now - created;
        if (age.TotalDays >= 365)
        {
            return $"{(int)(age.TotalDays / 365)}y ago";
        }
        if (age.TotalDays >= 30)
        {
            return $"{(int)(age.TotalDays / 30)}mo ago";
        }
        return $"{Math.Max(0, (int)age.TotalDays)}d ago";
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _ = ViewModel.LoadCommand.ExecuteAsync(parameter: null);
    }
}

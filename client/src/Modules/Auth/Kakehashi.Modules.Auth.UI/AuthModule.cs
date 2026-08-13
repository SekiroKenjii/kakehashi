using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.Messaging;
using Kakehashi.Application.Abstractions.Messaging;
using Kakehashi.Application.Abstractions.Security;
using Kakehashi.Modules.Auth.Application;
using Kakehashi.Modules.Auth.Application.Abstractions;
using Kakehashi.Modules.Auth.Application.Sessions.Events;
using Kakehashi.Modules.Auth.UI.Infrastructure;
using Kakehashi.Modules.Auth.UI.ViewModels;
using Kakehashi.Modules.Auth.UI.Views;
using Kakehashi.UI.Contracts;
using Kakehashi.UI.Contracts.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace Kakehashi.Modules.Auth.UI {
  /// <summary>
  /// Composition entry point for the Auth module: binds options, registers the application layer and
  /// the concrete OIDC adapters, replaces the host's access-token provider with the session-backed
  /// one, contributes the startup login gate and the forced re-sign-in on sign-out, and exposes the
  /// Account page through an avatar item in the shell's footer.
  /// </summary>
  public sealed class AuthModule : IModule {
    public string Name => "Auth";

    /// <summary>
    /// Marked required: the startup sign-in gate and the forced re-sign-in depend on this module,
    /// so it cannot be detached.
    /// </summary>
    public ModuleDescriptor Descriptor { get; } = new(
        "Account",
        "Identity, security activity and session management — sign out here or everywhere.",
        IsRequired: true,
        // The server calls this module "account", not "auth", because IDENTITY is reserved in
        // T-SQL. Written down, not derived: a permission key that drifts stops applying.
        AssignmentId: "account");

    public void RegisterServices(IServiceCollection services) {
      ArgumentNullException.ThrowIfNull(services);

      services.AddOptions<AuthOptions>().BindConfiguration(AuthOptions.SectionName);
      services.AddAuthApplication();

      // Concrete adapters for the application ports (adapters live in the UI layer).
      services.TryAddSingleton<ITokenStore, DpapiTokenStore>();
      // Shared between the authenticator (drives the flow) and the login view model (reopen browser).
      services.TryAddSingleton(provider => new SystemBrowser(
          provider.GetRequiredService<IOptions<AuthOptions>>().Value.RedirectUri));
      // Both are always registered and only the port's answer changes: RegisterServices runs before
      // configuration binds, so Auth:Mode is unreadable here, and in-app needs OIDC to refresh.
      services.TryAddSingleton<OidcInteractiveAuthenticator>();
      services.TryAddSingleton<InAppAuthenticator>();
      services.TryAddSingleton<IInteractiveAuthenticator>(provider =>
          provider.GetRequiredService<IOptions<AuthOptions>>().Value.Mode == AuthMode.Browser
              ? provider.GetRequiredService<OidcInteractiveAuthenticator>()
              : provider.GetRequiredService<InAppAuthenticator>());
      services.TryAddSingleton<IAccountGateway, AccountGateway>();
      services.TryAddSingleton<AuthSessionAccessor>();
      services.TryAddSingleton<IAuthSessionAccessor>(
          provider => provider.GetRequiredService<AuthSessionAccessor>());

      // Seam 1: replace the host's no-op access-token provider with the session-backed one.
      services.RemoveAll<IAccessTokenProvider>();
      services.AddSingleton<IAccessTokenProvider>(
          provider => provider.GetRequiredService<AuthSessionAccessor>());

      // Seam 2: contribute the startup login gate.
      services.AddSingleton<IAuthenticationGate, AuthenticationGate>();

      // Seam 3: after any sign-out, force a modal re-sign-in over the blurred main window.
      services.AddSingleton<ReauthenticationService>();
      services.AddTransient<
          INotificationHandler<UserSignedOutNotification>, SignedOutReauthenticationHandler>();

      services.AddTransient<LoginViewModel>();
      services.AddTransient<LoginWindow>();
      services.AddTransient<AccountViewModel>();
      services.AddTransient<AccountPage>();
      services.AddTransient<AccountFlyoutViewModel>();
      services.AddTransient<AccountFlyoutView>();
    }

    public IReadOnlyList<NavigationItem> GetNavigationItems() {
      // The avatar is shared between the item content and the flyout so the initials refresh
      // whenever the flyout opens (e.g. after re-signing in as a different user).
      PersonPicture? avatar = null;
      return [
        new NavigationItem("Account", "", typeof(AccountPage), NavigationItemPlacement.Footer) {
          ContentFactory = () => {
            avatar = new PersonPicture {
              Width = 28,
              Height = 28,
              HorizontalAlignment = HorizontalAlignment.Left,
              VerticalAlignment = VerticalAlignment.Center,
            };
            UpdateAvatar(avatar);
            return CreateAccountItemContent(avatar);
          },
          FlyoutFactory = () => CreateAccountFlyout(() => avatar),
        },
      ];
    }

    /// <summary>
    /// Lays the avatar and label out over the default item template's geometry so they line up
    /// with icon items (e.g. Settings) in every pane state. The shell gives the item a blank
    /// Icon, so the presenter's 40px icon column - with its icon box centered at x=20 - is
    /// present in both expanded and compact modes, and content starts 4px after it.
    /// The -44 margin re-bases this grid at the icon column's origin, the avatar is centered on
    /// the icon box, and the 44px first column puts the label back at the standard position.
    /// </summary>
    private static Grid CreateAccountItemContent(PersonPicture avatar) {
      var panel = new Grid { Margin = new Thickness(-44, 0, 0, 0) };
      panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
      panel.ColumnDefinitions.Add(
          new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

      avatar.Margin = new Thickness((40 - avatar.Width) / 2, 0, 0, 0);

      var label = new TextBlock {
        Text = ResolveAccountLabel(),
        VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis,
      };
      Grid.SetColumn(label, 1);

      // Keep the label and avatar in sync with sign-in / sign-out for the app's lifetime. The
      // messenger holds weak references, so the shell item can still be collected.
      WeakReferenceMessenger.Default.Register<TextBlock, AuthSessionChangedMessage>(
          label, static (recipient, _) => recipient.DispatcherQueue.TryEnqueue(
              () => recipient.Text = ResolveAccountLabel()));
      WeakReferenceMessenger.Default.Register<PersonPicture, AuthSessionChangedMessage>(
          avatar, static (recipient, _) => recipient.DispatcherQueue.TryEnqueue(
              () => UpdateAvatar(recipient)));

      panel.Children.Add(avatar);
      panel.Children.Add(label);
      return panel;
    }

    /// <summary>The shell item label: full name, falling back to email, then to "Account".</summary>
    private static string ResolveAccountLabel() {
      var session = ContractServices.Provider.GetRequiredService<IAuthSessionAccessor>().Current;
      if (!string.IsNullOrWhiteSpace(session?.DisplayName)) {
        return session.DisplayName;
      }
      return string.IsNullOrWhiteSpace(session?.Email) ? "Account" : session.Email;
    }

    private static Flyout CreateAccountFlyout(Func<PersonPicture?> avatarAccessor) {
      var view = ContractServices.Provider.GetRequiredService<AccountFlyoutView>();
      var flyout = new Flyout { Content = view, Placement = FlyoutPlacementMode.Right };

      flyout.Opening += async (_, _) => {
        await view.ViewModel.LoadCommand.ExecuteAsync(null);
        if (avatarAccessor() is { } avatar) {
          avatar.DisplayName = view.ViewModel.AvatarName;
        }
      };
      view.CloseRequested += flyout.Hide;
      return flyout;
    }

    private static void UpdateAvatar(PersonPicture avatar) {
      // A null display name leaves PersonPicture showing its generic person glyph.
      var session = ContractServices.Provider.GetRequiredService<IAuthSessionAccessor>().Current;
      avatar.DisplayName = session?.DisplayName;
      avatar.Foreground = Microsoft.UI.Xaml.Application
        .Current.Resources["TextOnAccentFillColorPrimaryBrush"] as SolidColorBrush;
      avatar.Background = Microsoft.UI.Xaml.Application
        .Current.Resources["AccentFillColorDefaultBrush"] as SolidColorBrush;
    }
  }
}

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
  public sealed class AuthModule : IModule {
    public string Name => "Auth";

    // Required: the startup sign-in gate and the forced re-sign-in depend on this module.
    public ModuleDescriptor Descriptor { get; } = new(
        "Account",
        "Identity, security activity and session management — sign out here or everywhere.",
        IsRequired: true,
        // The server calls this module "account", not "auth": IDENTITY is a reserved T-SQL word, so
        // the module ID had to be something else. Written down rather than derived, because a
        // permission key that drifts is a permission that stops applying.
        AssignmentId: "account");

    public void RegisterServices(IServiceCollection services) {
      ArgumentNullException.ThrowIfNull(services);

      services.AddOptions<AuthOptions>().BindConfiguration(AuthOptions.SectionName);
      services.AddAuthApplication();

      services.TryAddSingleton<ITokenStore, DpapiTokenStore>();
      // One instance: the authenticator drives the flow, the login view model reopens the browser
      // at the URL that flow is waiting on.
      services.TryAddSingleton(provider => new SystemBrowser(
          provider.GetRequiredService<IOptions<AuthOptions>>().Value.RedirectUri));
      // Which adapter answers IInteractiveAuthenticator is decided on first resolve, not here:
      // RegisterServices runs before configuration is bound, so Auth:Mode is not readable yet. The
      // in-app adapter needs the OIDC one for the refresh grant, so both are always registered.
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

      services.RemoveAll<IAccessTokenProvider>();
      services.AddSingleton<IAccessTokenProvider>(
          provider => provider.GetRequiredService<AuthSessionAccessor>());

      services.AddSingleton<IAuthenticationGate, AuthenticationGate>();

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
      // Captured so the item content and the flyout share one avatar: the initials refresh when
      // the flyout opens, e.g. after re-signing in as a different user.
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

    // Aligns with icon items (e.g. Settings) in every pane state. The shell gives the item a blank
    // Icon, so the presenter's 40px icon column - icon box centered at x=20 - exists in both
    // expanded and compact modes, and content starts 4px after it. The -44 margin re-bases this
    // grid at the icon column's origin, the avatar is centered on the icon box, and the 44px first
    // column puts the label back at the standard position.
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

      // Registered for the app's lifetime; the messenger holds weak references, so the shell item
      // can still be collected.
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

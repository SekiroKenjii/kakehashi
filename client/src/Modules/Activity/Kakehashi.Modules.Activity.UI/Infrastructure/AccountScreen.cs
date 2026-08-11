using System;
using Kakehashi.Modules.Activity.Application.Abstractions;
using Kakehashi.UI.Contracts.Services.Platform;

namespace Kakehashi.Modules.Activity.UI.Infrastructure {
  // Opens the Auth module's account screen by the key its page type derives.
  //
  // The one place in this module that names another module's screen. The key is what
  // INavigationService derives from a page's type name, so it is AccountPage — a string
  // with nothing checking it, which is exactly why it is here and not in a view model.
  public sealed class AccountScreen : IAccountScreen {
    private const string _pageKey = "AccountPage";

    private readonly INavigationService _navigation;

    public AccountScreen(INavigationService navigation) {
      ArgumentNullException.ThrowIfNull(navigation);
      _navigation = navigation;
    }

    public bool Open() {
      return _navigation.NavigateTo(_pageKey);
    }
  }
}

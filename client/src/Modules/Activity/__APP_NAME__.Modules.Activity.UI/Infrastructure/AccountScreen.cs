using System;
using __ROOT_NAMESPACE__.Modules.Activity.Application.Abstractions;
using __ROOT_NAMESPACE__.UI.Contracts.Services.Platform;

namespace __ROOT_NAMESPACE__.Modules.Activity.UI.Infrastructure;

/// <summary>Opens the Auth module's account screen by the key its page type derives.</summary>
/// <remarks>
/// The one place in this module that names another module's screen. The key is what
/// <c>INavigationService</c> derives from a page's type name, so it is <c>AccountPage</c> — a string
/// with nothing checking it, which is exactly why it is here and not in a view model.
/// </remarks>
public sealed class AccountScreen : IAccountScreen
{
    private const string _pageKey = "AccountPage";

    private readonly INavigationService _navigation;

    public AccountScreen(INavigationService navigation)
    {
        ArgumentNullException.ThrowIfNull(navigation);
        _navigation = navigation;
    }

    public bool Open()
    {
        return _navigation.NavigateTo(_pageKey);
    }
}

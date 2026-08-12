using System;
using Microsoft.UI.Xaml.Controls;

namespace Kakehashi.UI.Contracts.Services.Platform {
  public interface INavigationService : IUiContractService, ISingletonDependency {
    IObservable<NavigationEvent> OnNavigated { get; }

    bool CanGoBack { get; }

    // Call once, when the shell's frame loads.
    void Initialize(Frame frame);

    // The navigation key is derived from the page type name, not chosen by the caller.
    void Register<TPage>() where TPage : Page;

    void Register(params ReadOnlySpan<Type> pageTypes);

    // For code that builds navigation UI, so the key it tags an item with is the key Register
    // derived for the page.
    string GetPageKey(Type pageType);

    // Pages are resolved from the container, so they get constructor injection.
    bool NavigateTo(string pageKey, params ReadOnlySpan<object> args);

    void GoBack();

    void ClearBackStack();
  }
}

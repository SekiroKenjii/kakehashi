using System;
using Microsoft.UI.Xaml.Controls;

namespace __ROOT_NAMESPACE__.UI.Contracts.Services.Platform;

/// <summary>Shows module pages, resolved from the container, in the shell's content frame.</summary>
public interface INavigationService : IUiContractService, ISingletonDependency
{
    /// <summary>
    /// Emits a <see cref="NavigationEvent"/> whenever the shell navigates to a new page. Other
    /// components (e.g. the shell view model, the header behavior) react to this to update the UI.
    /// </summary>
    IObservable<NavigationEvent> OnNavigated { get; }

    /// <summary>Whether there is a page in the navigation back stack.</summary>
    bool CanGoBack { get; }

    /// <summary>Binds the service to the shell's content <see cref="Frame"/>. Call once, on load.</summary>
    void Initialize(Frame frame);

    /// <summary>
    /// Registers a page type with the service, associating it with a key derived from the type
    /// name. The key identifies the page type when navigating.
    /// </summary>
    /// <typeparam name="TPage">The type of the page to register.</typeparam>
    void Register<TPage>() where TPage : Page;

    /// <summary>
    /// Registers pages with the service, associating them with keys derived from their type names.
    /// The keys identify the page types when navigating.
    /// </summary>
    /// <param name="pageTypes">The types of the pages to register.</param>
    void Register(params ReadOnlySpan<Type> pageTypes);

    /// <summary>
    /// Returns the navigation key the service derives for a page type (the same key used by
    /// <see cref="Register"/>). Callers that build navigation UI use this so the key they
    /// tag an item with matches the key the page is registered under.
    /// </summary>
    /// <param name="pageType">The page type to derive a key for.</param>
    string GetPageKey(Type pageType);

    /// <summary>
    /// Navigates to the page registered under <paramref name="pageKey"/>, resolving it from the
    /// container (so pages get constructor injection) and passing optional arguments.
    /// </summary>
    /// <returns><see langword="true"/> if navigation succeeded; otherwise <see langword="false"/>.</returns>
    bool NavigateTo(string pageKey, params ReadOnlySpan<object> args);

    /// <summary>Navigates back to the previous page in the back stack, if any.</summary>
    void GoBack();

    /// <summary>Clears the navigation back stack.</summary>
    void ClearBackStack();
}

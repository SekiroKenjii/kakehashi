using System;

namespace Kakehashi.UI.Contracts.Services.Platform {
  /// <summary>
  /// Raised by <see cref="INavigationService"/> after a successful navigation. It carries enough to
  /// react without coupling to the framework's (non-constructable) <c>NavigationEventArgs</c>, so the
  /// navigation service can resolve pages from the container and maintain its own back stack.
  /// </summary>
  /// <param name="PageKey">The key the page was registered/navigated under (used to sync the shell selection).</param>
  /// <param name="SourcePageType">The type of the page navigated to.</param>
  /// <param name="Content">The page instance now shown in the shell frame.</param>
  /// <param name="Parameters">The arguments passed to the navigation, if any.</param>
  public sealed record NavigationEvent(
      string PageKey, Type SourcePageType, object Content, object[] Parameters);
}

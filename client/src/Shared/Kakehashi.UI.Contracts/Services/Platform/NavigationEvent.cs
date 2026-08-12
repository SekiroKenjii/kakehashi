using System;

namespace Kakehashi.UI.Contracts.Services.Platform {
  // Stands in for the framework's NavigationEventArgs, which cannot be constructed, so the
  // navigation service can resolve pages from the container and keep its own back stack.
  public sealed record NavigationEvent(
      string PageKey, Type SourcePageType, object Content, object[] Parameters);
}

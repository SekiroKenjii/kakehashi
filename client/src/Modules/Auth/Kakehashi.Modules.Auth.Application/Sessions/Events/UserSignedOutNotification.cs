using Kakehashi.Application.Abstractions.Messaging;

namespace Kakehashi.Modules.Auth.Application.Sessions.Events {
  // The sanctioned cross-module path: other modules handle this with INotificationHandler<T>
  // rather than referencing the Auth module.
  public sealed record UserSignedOutNotification : INotification;
}

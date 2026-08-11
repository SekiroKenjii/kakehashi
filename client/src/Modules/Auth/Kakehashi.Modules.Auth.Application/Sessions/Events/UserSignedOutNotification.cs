using Kakehashi.Application.Abstractions.Messaging;

namespace Kakehashi.Modules.Auth.Application.Sessions.Events {
  // Published after the user signs out so other modules can clear user-scoped state. This is the
  // sanctioned cross-module collaboration path: modules subscribe with an
  // INotificationHandler{T} rather than referencing the Auth module directly.
  public sealed record UserSignedOutNotification : INotification;
}

using __ROOT_NAMESPACE__.Application.Abstractions.Messaging;

namespace __ROOT_NAMESPACE__.Modules.Auth.Application.Sessions.Events;

/// <summary>
/// Published after the user signs out so other modules can clear user-scoped state. This is the
/// sanctioned cross-module collaboration path: modules subscribe with an
/// <see cref="INotificationHandler{T}"/> rather than referencing the Auth module directly.
/// </summary>
public sealed record UserSignedOutNotification : INotification;

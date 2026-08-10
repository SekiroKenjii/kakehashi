namespace Kakehashi.Application.Abstractions.Messaging {
  /// <summary>
  /// An event delivered to zero or more handlers. Used for integration events that cross module
  /// boundaries; for in-module reactions prefer domain events and <see cref="IDomainEventHandler{T}"/>.
  /// </summary>
  public interface INotification {
  }
}

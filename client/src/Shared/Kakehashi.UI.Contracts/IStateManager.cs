using System;

namespace Kakehashi.UI.Contracts {
  /// <summary>
  /// Represents a state manager that manages the state of a system. It provides methods to get the current state, subscribe to state changes, and update the state. The implementation of this interface is platform-specific, as it needs to interact with the XAML framework to manage the state in the UI.
  /// </summary>
  /// <typeparam name="T">The type that represents the state of the system.</typeparam>
  public interface IStateManager<T> : ITransientDependency, IDisposable {
    /// <summary>
    /// Gets the current state of the system. The state is represented by a generic type T, which can be any type that represents the state of the system. The implementation of this property is platform-specific, as it needs to interact with the XAML framework to get the current state from the UI.
    /// </summary>
    T CurrentState { get; }

    /// <summary>
    /// Returns an observable that emits the current state and subsequent state changes. This allows subscribers to react to state changes in real-time. The implementation of this method is platform-specific, as it needs to interact with the XAML framework to observe state changes in the UI.
    /// </summary>
    /// <returns>An observable that emits the current state and subsequent state changes.</returns>
    IObservable<T> AsObservable();

    /// <summary>
    /// Subscribes to state changes and invokes the provided callback whenever the state changes. The callback receives the new state as a parameter. The implementation of this method is platform-specific, as it needs to interact with the XAML framework to subscribe to state changes in the UI.
    /// </summary>
    /// <param name="onNext">The callback to invoke when the state changes.</param>
    /// <returns>A disposable that unsubscribes from state changes when disposed.</returns>
    IDisposable Subscribe(Action<T> onNext);

    /// <summary>
    /// Updates the state of the system. The new state is represented by a generic type T, which can be any type that represents the state of the system. The implementation of this method is platform-specific, as it needs to interact with the XAML framework to update the state in the UI.
    /// </summary>
    /// <param name="state">The new state to set.</param>
    void Next(T state);
  }
}

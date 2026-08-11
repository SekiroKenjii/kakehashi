using System;

namespace Kakehashi.UI.Contracts {
  /// <summary>
  /// A value that can be read, watched and replaced.
  /// </summary>
  /// <typeparam name="T">The state.</typeparam>
  public interface IStateManager<T> : ITransientDependency, IDisposable {
    /// <summary>The state as it stands.</summary>
    T CurrentState { get; }

    /// <summary>
    /// The state over time, beginning with the current one.
    /// </summary>
    /// <remarks>
    /// A subscriber is handed the state it arrived to before it is handed any change. That is what
    /// makes this usable for binding: there is no window in which a subscriber has nothing.
    /// </remarks>
    IObservable<T> AsObservable();

    /// <summary>Runs a callback on the current state and on every later one.</summary>
    /// <param name="onNext">What to do with each state.</param>
    /// <returns>Disposing this stops the callbacks.</returns>
    IDisposable Subscribe(Action<T> onNext);

    /// <summary>Replaces the state.</summary>
    /// <param name="state">The new state.</param>
    void Next(T state);
  }
}

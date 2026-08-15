using System;

namespace __ROOT_NAMESPACE__.UI.Contracts;

/// <summary>
/// An observable state cell: holds a current value and pushes every update to subscribers.
/// </summary>
/// <typeparam name="T">The state type.</typeparam>
public interface IStateManager<T> : ITransientDependency, IDisposable
{
    /// <summary>The most recently set state.</summary>
    T CurrentState { get; }

    /// <summary>
    /// Returns an observable that emits the current state immediately, then every update.
    /// </summary>
    IObservable<T> AsObservable();

    /// <summary>
    /// Invokes <paramref name="onNext"/> with the current state immediately, then on every update.
    /// </summary>
    /// <returns>A disposable that ends the subscription.</returns>
    IDisposable Subscribe(Action<T> onNext);

    /// <summary>Sets the state and notifies subscribers.</summary>
    void Next(T state);
}

using System;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using __ROOT_NAMESPACE__.UI.Contracts;

namespace __ROOT_NAMESPACE__.App.Core;

public sealed partial class StateManager<T>(T initialState) : IStateManager<T>
{
    private readonly BehaviorSubject<T> _subject = new(initialState);

    public T CurrentState => _subject.Value;

    public IObservable<T> AsObservable()
    {
        return _subject.AsObservable();
    }

    public void Next(T state)
    {
        _subject.OnNext(state);
    }

    public IDisposable Subscribe(Action<T> onNext)
    {
        return _subject.Subscribe(onNext);
    }

    public void Dispose()
    {
        if (_subject.IsDisposed)
        {
            return;
        }

        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (disposing)
        {
            _subject.Dispose();
        }
    }
}

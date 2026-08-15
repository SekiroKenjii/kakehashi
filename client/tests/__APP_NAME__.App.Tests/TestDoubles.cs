using System.Collections.Generic;
using __ROOT_NAMESPACE__.UI.Contracts.Services.Platform;
using Microsoft.Extensions.Options;

namespace __ROOT_NAMESPACE__.App.Tests;

/// <summary>
/// An in-memory <see cref="ILocalSettingsService"/> that stores values as-is, so tests can both
/// pre-seed state and observe what production code persists.
/// </summary>
internal sealed class InMemoryLocalSettings : ILocalSettingsService
{
    private readonly Dictionary<string, object?> _values = new();

    public T? Read<T>(string key)
    {
        return _values.TryGetValue(key, out var value) ? (T?)value : default;
    }

    public void Save<T>(string key, T value)
    {
        _values[key] = value;
    }
}

internal sealed class StubOptions<T>(T value) : IOptions<T> where T : class
{
    public T Value { get; } = value;
}

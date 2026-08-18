using System;

namespace __ROOT_NAMESPACE__.UI.Contracts.Services.Platform;

/// <summary>Where the app's accent colour comes from.</summary>
public enum AccentSource
{
    /// <summary>The accent the user chose in Windows. The default.</summary>
    Windows = 0,

    /// <summary>The accent this project was scaffolded with.</summary>
    App = 1,
}

/// <summary>
/// Reads, applies and persists the accent choice. The implementation overrides the system accent
/// resources when the project's own accent is chosen, and persists the choice across runs.
/// </summary>
public interface IAccentService : IUiContractService, ISingletonDependency
{
    /// <summary>The currently applied source.</summary>
    AccentSource Accent { get; }

    /// <summary>
    /// Whether the project declares an accent of its own. When it does not, the setting has one
    /// working choice and the page hides it rather than offering a switch that cannot switch.
    /// </summary>
    bool HasAppAccent { get; }

    /// <summary>Emits the new source whenever it changes.</summary>
    IObservable<AccentSource> OnAccentChanged { get; }

    /// <summary>Loads the persisted choice and applies it. Call once, after the main window exists.</summary>
    void Initialize();

    /// <summary>Applies and persists the given source.</summary>
    void SetAccent(AccentSource source);
}

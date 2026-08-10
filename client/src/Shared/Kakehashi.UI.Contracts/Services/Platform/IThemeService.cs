using System;
using Microsoft.UI.Xaml;

namespace Kakehashi.UI.Contracts.Services.Platform {
  /// <summary>
  /// Reads, applies and persists the app's theme. The implementation applies the theme to the main
  /// window's content root and the title-bar caption buttons, and persists the choice across runs.
  /// </summary>
  public interface IThemeService : IUiContractService, ISingletonDependency {
    /// <summary>The currently applied theme.</summary>
    ElementTheme Theme { get; }

    /// <summary>Emits the new theme whenever it changes.</summary>
    IObservable<ElementTheme> OnThemeChanged { get; }

    /// <summary>Loads the persisted theme and applies it. Call once, after the main window exists.</summary>
    void Initialize();

    /// <summary>Applies and persists the given theme.</summary>
    void SetTheme(ElementTheme theme);
  }
}

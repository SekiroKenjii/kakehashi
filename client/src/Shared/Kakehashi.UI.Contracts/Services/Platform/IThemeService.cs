using System;
using Microsoft.UI.Xaml;

namespace Kakehashi.UI.Contracts.Services.Platform {
  public interface IThemeService : IUiContractService, ISingletonDependency {
    ElementTheme Theme { get; }

    IObservable<ElementTheme> OnThemeChanged { get; }

    // Loads the persisted theme and applies it. Call once, after the main window exists.
    void Initialize();

    void SetTheme(ElementTheme theme);
  }
}

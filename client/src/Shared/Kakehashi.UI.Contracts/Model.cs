using CommunityToolkit.Mvvm.ComponentModel;

namespace Kakehashi.UI.Contracts {
  /// <summary>
  /// Base class for bindable models that need change notification without the
  /// <see cref="ViewModel"/> lifecycle.
  /// </summary>
  public abstract class Model : ObservableObject;
}

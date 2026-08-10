using CommunityToolkit.Mvvm.ComponentModel;

namespace Kakehashi.UI.Contracts {
  /// <summary>
  /// Base class for all models in the application. This class can be extended to include common properties or methods that all models should have. It inherits from ObservableObject to support property change notifications, which is useful for data binding in MVVM architecture.
  /// </summary>
  public abstract class Model : ObservableObject;
}

namespace Kakehashi.UI.Contracts {
  /// <summary>
  /// Marker interface for singleton dependencies. Implement this interface on services that should be
  /// registered with a singleton lifetime in the dependency injection container.
  /// </summary>
  public interface ISingletonDependency;
}

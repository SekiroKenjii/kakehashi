namespace Kakehashi.UI.Contracts {
  /// <summary>
  /// Marker interface for transient dependencies. Implement this interface on services that should be
  /// registered with a transient lifetime in the dependency injection container.
  /// </summary>
  public interface ITransientDependency;
}

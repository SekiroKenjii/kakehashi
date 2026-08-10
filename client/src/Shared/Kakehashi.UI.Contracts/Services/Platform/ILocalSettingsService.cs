namespace Kakehashi.UI.Contracts.Services.Platform {
  /// <summary>
  /// Persists small key/value settings locally. The default implementation stores JSON under the
  /// user's local app-data folder so it works for the unpackaged app (no <c>ApplicationData</c>).
  /// </summary>
  public interface ILocalSettingsService : IUiContractService, ISingletonDependency {
    /// <summary>Reads a value, or the type default when the key is absent.</summary>
    T? Read<T>(string key);

    /// <summary>Writes a value and persists it.</summary>
    void Save<T>(string key, T value);
  }
}

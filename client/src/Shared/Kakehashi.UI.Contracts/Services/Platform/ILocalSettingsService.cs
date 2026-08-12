namespace Kakehashi.UI.Contracts.Services.Platform {
  // The default implementation stores JSON under the user's local app-data folder, because the
  // unpackaged app has no ApplicationData.
  public interface ILocalSettingsService : IUiContractService, ISingletonDependency {
    // An absent key yields the type default rather than an error.
    T? Read<T>(string key);

    void Save<T>(string key, T value);
  }
}

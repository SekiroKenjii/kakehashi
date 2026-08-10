using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Kakehashi.UI.Contracts.Services.Platform;

namespace Kakehashi.App.Services {
  /// <summary>
  /// Persists small settings as JSON under the user's local app-data folder. This works for the
  /// unpackaged app (which cannot use <c>Windows.Storage.ApplicationData</c>); a packaged app could
  /// swap in an <c>ApplicationData</c>-backed implementation without touching callers.
  /// </summary>
  public sealed class LocalSettingsService : ILocalSettingsService {
    private readonly string _path;
    private readonly Dictionary<string, JsonElement> _cache;

    public LocalSettingsService() {
      var directory = Path.Combine(
          Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kakehashi");
      Directory.CreateDirectory(directory);
      _path = Path.Combine(directory, "settings.json");
      _cache = Load(_path);
    }

    public T? Read<T>(string key) {
      ArgumentException.ThrowIfNullOrEmpty(key);
      return _cache.TryGetValue(key, out var element) ? element.Deserialize<T>() : default;
    }

    public void Save<T>(string key, T value) {
      ArgumentException.ThrowIfNullOrEmpty(key);
      _cache[key] = JsonSerializer.SerializeToElement(value);

      try {
        File.WriteAllText(_path, JsonSerializer.Serialize(_cache));
      } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
        // Best-effort persistence; an unwritable settings file should not crash the app.
      }
    }

    private static Dictionary<string, JsonElement> Load(string path) {
      try {
        if (File.Exists(path)) {
          var json = File.ReadAllText(path);
          return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json) ?? [];
        }
      } catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) {
        // Corrupt or unreadable settings: start fresh rather than failing startup.
      }

      return [];
    }
  }
}

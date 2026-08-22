using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace __ROOT_NAMESPACE__.App.Plugins;

/// <summary>
/// What is installed, what is waiting for a restart, and what comes out at the next one.
/// </summary>
/// <remarks>
/// A file of its own rather than a key in the settings store: it is written by an install and read
/// before anything else at startup, and a corrupt or absent one has to mean "no plugins" rather
/// than "no settings".
/// </remarks>
public sealed class PluginState
{
    private readonly string _path;
    private readonly Dictionary<string, PluginRecord> _records;

    private PluginState(string path, Dictionary<string, PluginRecord> records)
    {
        _path = path;
        _records = records;
    }

    public IReadOnlyList<PluginRecord> Records => [.. _records.Values];

    /// <summary>
    /// Reads the state, or an empty one.
    /// </summary>
    /// <remarks>
    /// An unreadable file is treated as empty rather than as a failure. The alternative is an
    /// application that will not start because of a plugin, which is the one outcome the whole
    /// design is arranged to avoid.
    /// </remarks>
    public static PluginState Load(PluginPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var records = new Dictionary<string, PluginRecord>(StringComparer.Ordinal);

        try
        {
            if (File.Exists(paths.StateFile))
            {
                using var stream = File.OpenRead(paths.StateFile);
                var stored = JsonSerializer.Deserialize(stream, PluginStateJsonContext.Default.PluginRecordArray);

                foreach (var record in stored ?? [])
                {
                    if (record is not null && record.PluginID.Length > 0)
                    {
                        records[record.PluginID] = record;
                    }
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            records.Clear();
        }

        return new PluginState(paths.StateFile, records);
    }

    public PluginRecord? Find(string pluginID)
    {
        return _records.GetValueOrDefault(pluginID);
    }

    public void Put(PluginRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        _records[record.PluginID] = record;
    }

    public void Remove(string pluginID)
    {
        _ = _records.Remove(pluginID);
    }

    /// <summary>Writes the state. Reports failure rather than throwing; the caller decides.</summary>
    public bool TrySave()
    {
        try
        {
            var directory = Path.GetDirectoryName(_path);

            if (directory is not null)
            {
                Directory.CreateDirectory(directory);
            }
            var ordered = _records.Values
                .OrderBy(record => record.PluginID, StringComparer.Ordinal)
                .ToArray();

            using var stream = File.Create(_path);
            JsonSerializer.Serialize(stream, ordered, PluginStateJsonContext.Default.PluginRecordArray);

            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(PluginRecord[]))]
internal sealed partial class PluginStateJsonContext : JsonSerializerContext
{
}

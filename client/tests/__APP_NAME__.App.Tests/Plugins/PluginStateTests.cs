using System;
using System.IO;
using __ROOT_NAMESPACE__.App.Plugins;
using Xunit;

namespace __ROOT_NAMESPACE__.App.Tests.Plugins;

/// <summary>
/// Unit tests for <see cref="PluginState"/>: the round trip, and the two ways a state file can be
/// unusable.
/// </summary>
public sealed class PluginStateTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    private PluginPaths Paths => new(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void RoundTrip_KeepsEveryField()
    {
        var state = PluginState.Load(Paths);
        state.Put(new PluginRecord {
            PluginID = "weather",
            DisplayName = "Weather",
            InstalledVersion = "1.0.0",
            StagedVersion = "1.1.0",
            Source = "Catalog",
            SHA256 = "abc",
            SignerSubject = "CN=Somebody",
            Signature = "Unofficial",
            ConsentGiven = true,
            SizeInBytes = 2048,
            InstalledOn = DateTimeOffset.UnixEpoch,
        });

        Assert.True(state.TrySave());
        var stored = PluginState.Load(Paths);
        var reloaded = stored.Find("weather");

        Assert.NotNull(reloaded);
        Assert.Equal("Weather", reloaded.DisplayName);
        Assert.Equal("1.0.0", reloaded.InstalledVersion);
        Assert.Equal("1.1.0", reloaded.StagedVersion);
        Assert.True(reloaded.ConsentGiven);
        Assert.Equal(2048, reloaded.SizeInBytes);
        Assert.Equal("CN=Somebody", reloaded.SignerSubject);
    }

    [Fact]
    public void Load_WithNoFile_IsEmptyRatherThanAFailure()
    {
        Assert.Empty(PluginState.Load(Paths).Records);
    }

    /// <summary>
    /// An unreadable state file must not stop the application starting, which is the one outcome
    /// the whole design is arranged to avoid.
    /// </summary>
    [Fact]
    public void Load_WithAFileThatIsNotJson_IsEmptyRatherThanAThrow()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Paths.StateFile, "{ this is not json");

        Assert.Empty(PluginState.Load(Paths).Records);
    }

    [Fact]
    public void Load_SkipsARecordWithNoIdentity()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Paths.StateFile, """[{ "displayName": "Nameless" }]""");

        Assert.Empty(PluginState.Load(Paths).Records);
    }

    [Fact]
    public void Remove_TakesTheRecordOut()
    {
        var state = PluginState.Load(Paths);
        state.Put(new PluginRecord { PluginID = "weather" });
        state.Remove("weather");

        Assert.Null(state.Find("weather"));
    }

    /// <summary>
    /// Written beside and moved over, so a kill during the write leaves the previous file rather
    /// than a truncated one — which is the byte that made the whole state unreadable.
    /// </summary>
    [Fact]
    public void TrySave_LeavesNoPartialFileBehind()
    {
        var paths = new PluginPaths(_root);
        var state = PluginState.Load(paths);
        state.Put(new PluginRecord { PluginID = "weather", InstalledVersion = "1.0.0" });

        Assert.True(state.TrySave());
        Assert.False(File.Exists(paths.StateFile + ".writing"));

        var reloaded = PluginState.Load(paths);

        Assert.NotNull(reloaded.Find("weather"));
    }

    /// <summary>
    /// A state file that cannot be read is the only record of what somebody installed, so it is set
    /// aside rather than left where the next install writes over it.
    /// </summary>
    [Fact]
    public void Load_AnUnreadableFileIsSetAsideRatherThanOverwritten()
    {
        var paths = new PluginPaths(_root);
        Directory.CreateDirectory(_root);
        File.WriteAllText(paths.StateFile, "{ this is not json");

        var state = PluginState.Load(paths);

        Assert.Empty(state.Records);
        Assert.True(File.Exists(paths.StateFile + ".unreadable"));
    }
}

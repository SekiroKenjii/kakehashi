namespace __ROOT_NAMESPACE__.App.Plugins;

/// <summary>The deployment's answer to whether this installation loads plugins at all.</summary>
/// <remarks>
/// It defaults to on, because a build with nothing installed loads nothing and the switch is there
/// for the case that matters: a plugin, or the machinery that resolves one, misbehaving on a
/// machine somebody is using. Turning it off is a line of configuration rather than a new build.
/// </remarks>
public sealed class PluginOptions
{
    /// <summary>The configuration section this binds to.</summary>
    public const string SectionName = "Plugins";

    public bool Enabled { get; set; } = true;
}

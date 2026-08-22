using System;
using System.Globalization;
using System.Reflection;

namespace __ROOT_NAMESPACE__.PluginSdk.Abstractions;

/// <summary>
/// The plugin contract's version: the host's major and minor, and nothing else.
/// </summary>
/// <remarks>
/// It is the assembly version this project is stamped with, which
/// <c>client/Directory.Build.props</c> fixes at <c>major.minor.0.0</c> so a patch release never
/// changes what a plugin binds to. A plugin declares the oldest host it runs on and the loader
/// refuses an older one before the assembly is loaded, which turns a version mismatch into a
/// sentence on the row instead of a type-load failure inside XAML.
/// </remarks>
public readonly record struct PluginSdkVersion(int Major, int Minor)
    : IComparable<PluginSdkVersion>
{
    private static readonly PluginSdkVersion _current = FromAssembly(
        typeof(PluginSdkVersion).Assembly);

    /// <summary>The version of the SDK this assembly is.</summary>
    public static PluginSdkVersion Current => _current;

    public static bool operator <(PluginSdkVersion left, PluginSdkVersion right)
    {
        return left.CompareTo(right) < 0;
    }

    public static bool operator <=(PluginSdkVersion left, PluginSdkVersion right)
    {
        return left.CompareTo(right) <= 0;
    }

    public static bool operator >(PluginSdkVersion left, PluginSdkVersion right)
    {
        return left.CompareTo(right) > 0;
    }

    public static bool operator >=(PluginSdkVersion left, PluginSdkVersion right)
    {
        return left.CompareTo(right) >= 0;
    }

    /// <summary>Parses <c>major.minor</c>. Anything else, including a third part, fails.</summary>
    public static bool TryParse(string? text, out PluginSdkVersion version)
    {
        version = default;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Split('.');

        if (parts.Length != 2)
        {
            return false;
        }

        if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minor))
        {
            return false;
        }
        version = new PluginSdkVersion(major, minor);

        return true;
    }

    public int CompareTo(PluginSdkVersion other)
    {
        var major = Major.CompareTo(other.Major);

        return major != 0 ? major : Minor.CompareTo(other.Minor);
    }

    public override string ToString()
    {
        return string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}");
    }

    private static PluginSdkVersion FromAssembly(Assembly assembly)
    {
        var version = assembly.GetName().Version;

        return version is null
            ? new PluginSdkVersion(0, 0)
            : new PluginSdkVersion(version.Major, version.Minor);
    }
}

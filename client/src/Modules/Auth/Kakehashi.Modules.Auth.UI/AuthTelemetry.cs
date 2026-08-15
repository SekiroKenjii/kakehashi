using System.Diagnostics;

namespace Kakehashi.Modules.Auth.UI;

/// <summary>
/// The Auth module's tracing source. Register it for export in <c>AddObservability</c> with
/// <c>.AddSource("Kakehashi.Modules.Auth")</c>.
/// </summary>
public static class AuthTelemetry
{
    public const string SourceName = "Kakehashi.Modules.Auth";

    public static readonly ActivitySource Source = new(SourceName);
}

using System.Diagnostics;

namespace __ROOT_NAMESPACE__.Modules.Auth.UI;

/// <summary>
/// The Auth module's tracing source. Register it for export in <c>AddObservability</c> with
/// <c>.AddSource("__APP_NAME__.Modules.Auth")</c>.
/// </summary>
public static class AuthTelemetry
{
    public const string SourceName = "__APP_NAME__.Modules.Auth";

    public static readonly ActivitySource Source = new(SourceName);
}

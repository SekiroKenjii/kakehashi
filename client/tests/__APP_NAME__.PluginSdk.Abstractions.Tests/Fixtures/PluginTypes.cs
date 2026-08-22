namespace __ROOT_NAMESPACE__.PluginSdk.Abstractions.Tests.Fixtures;

/// <summary>A page named the way the navigation service requires.</summary>
public sealed class WeatherPage : Microsoft.UI.Xaml.Controls.Page;

/// <summary>A page whose key would be empty, which is what the suffix rule exists to catch.</summary>
public sealed class Forecast : Microsoft.UI.Xaml.Controls.Page;

public sealed class WeatherModule : UI.Contracts.IModule;

/// <summary>What the XAML compiler emits into an assembly carrying compiled markup.</summary>
public sealed class WeatherXamlMetaDataProvider : Microsoft.UI.Xaml.Markup.IXamlMetadataProvider;

/// <summary>Neither a page nor a module, so nothing should say it is either.</summary>
public sealed class WeatherSettings;

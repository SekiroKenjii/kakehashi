using System;
using System.Reflection;
using __ROOT_NAMESPACE__.SharedKernel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Markup;
using Microsoft.Windows.ApplicationModel.Resources;

namespace __ROOT_NAMESPACE__.PluginSdk.Xaml;

/// <summary>
/// Makes XAML compiled into a plugin assembly resolvable inside this application: its binary XAML,
/// and the types its markup names.
/// </summary>
/// <remarks>
/// WinUI resolves an <c>ms-appx:///</c> URI through a resource manager built from the application's
/// own index, which cannot see a file that was not part of this build. Two seams close that gap, and
/// they are independent — a plugin whose markup uses only framework and host types needs the first
/// alone.
/// <para>
/// Ordering is load-bearing. <see cref="Attach"/> must run in the application's constructor, because
/// the framework asks for its resource manager once per UI thread before the first launch. The
/// packages themselves may be added long afterwards: the fallback is consulted per failed lookup,
/// not once at startup.
/// </para>
/// </remarks>
public sealed class PluginXamlHost
{
    private readonly PluginResourceFallback _resources = new();

    private Application? _application;

    /// <summary>Takes over resource resolution for the application. Call from its constructor.</summary>
    public void Attach(Application application)
    {
        ArgumentNullException.ThrowIfNull(application);
        _application = application;
        application.ResourceManagerRequested += OnResourceManagerRequested;
    }

    /// <summary>Adds one plugin's resource index, which is where its compiled XAML lives.</summary>
    public Result AddPackage(string resourceIndexPath)
    {
        return _resources.Add(resourceIndexPath);
    }

    /// <summary>
    /// Registers the XAML types a plugin declares. Succeeds and does nothing when it declares none.
    /// </summary>
    public Result AddMetadataProvider(Assembly pluginAssembly)
    {
        ArgumentNullException.ThrowIfNull(pluginAssembly);

        if (_application is null)
        {
            return Result.Failure(PluginXamlErrors.NotAttached);
        }
        var name = pluginAssembly.GetName().Name ?? string.Empty;
        IXamlMetadataProvider? provider;
        string reason;

        try
        {
            provider = XamlMetadataBridge.FindProvider(pluginAssembly, out reason);
        }
        catch (ReflectionTypeLoadException exception)
        {
            return Result.Failure(PluginXamlErrors.MetadataProviderUnusable(name, exception.Message));
        }

        if (provider is not null)
        {
            return XamlMetadataBridge.Add(_application, provider);
        }

        return reason.Length == 0
            ? Result.Success()
            : Result.Failure(PluginXamlErrors.MetadataProviderUnusable(name, reason));
    }

    private void OnResourceManagerRequested(object sender, ResourceManagerRequestedEventArgs e)
    {
        var manager = new ResourceManager();
        manager.ResourceNotFound += _resources.OnResourceNotFound;
        e.CustomResourceManager = manager;
    }
}

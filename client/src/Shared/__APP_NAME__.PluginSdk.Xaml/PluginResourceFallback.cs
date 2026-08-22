using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using __ROOT_NAMESPACE__.SharedKernel;
using Microsoft.Windows.ApplicationModel.Resources;

namespace __ROOT_NAMESPACE__.PluginSdk.Xaml;

/// <summary>
/// The resource indexes of the loaded plugins, consulted when the application's own index has no
/// answer.
/// </summary>
/// <remarks>
/// A plugin's compiled XAML lives in the plugin's own <c>.pri</c>, which the resource manager built
/// from the application's index knows nothing about. The manager raises
/// <see cref="ResourceManager.ResourceNotFound"/> for a name it cannot resolve, and a candidate
/// produced by one manager satisfies a miss raised by another — which is the whole of the
/// mechanism.
/// <para>
/// The event is raised once per WinUI thread, so reads take a snapshot rather than hold the list:
/// indexes are added while the application starts and read for the life of the process.
/// </para>
/// </remarks>
internal sealed class PluginResourceFallback
{
    private readonly Lock _gate = new();

    /// <summary>Kept so the managers outlive the maps they produced.</summary>
    private readonly List<ResourceManager> _managers = [];

    private ResourceMap[] _maps = [];

    public Result Add(string resourceIndexPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(resourceIndexPath);

        // A path that does not exist yields an index with nothing in it rather than a failure, so
        // the file has to be checked here or the plugin fails at its first navigation instead.
        if (!File.Exists(resourceIndexPath))
        {
            return Result.Failure(PluginXamlErrors.ResourceIndexMissing(resourceIndexPath));
        }

        ResourceManager manager;
        ResourceMap map;

        try
        {
            manager = new ResourceManager(resourceIndexPath);
            map = manager.MainResourceMap;
        }
        catch (Exception exception) when (exception is COMException or IOException or ArgumentException)
        {
            return Result.Failure(PluginXamlErrors.ResourceIndexUnreadable(resourceIndexPath));
        }

        if (map.ResourceCount == 0)
        {
            return Result.Failure(PluginXamlErrors.ResourceIndexEmpty(resourceIndexPath));
        }

        lock (_gate)
        {
            _managers.Add(manager);
            _maps = [.. _maps, map];
        }

        return Result.Success();
    }

    public void OnResourceNotFound(ResourceManager sender, ResourceNotFoundEventArgs e)
    {
        var maps = _maps;

        foreach (var map in maps)
        {
            var candidate = map.TryGetValue(e.Name, e.Context);

            if (candidate is not null)
            {
                e.SetResolvedCandidate(candidate);

                return;
            }
        }
    }
}

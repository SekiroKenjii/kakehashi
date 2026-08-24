using System;
using System.Collections.Generic;
using System.Reflection;
using __ROOT_NAMESPACE__.SharedKernel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Markup;

namespace __ROOT_NAMESPACE__.PluginSdk.Xaml;

/// <summary>
/// Adds a plugin's XAML metadata provider to the list this application's own generated provider
/// falls back to, so markup in the plugin can name types the plugin declares.
/// </summary>
/// <remarks>
/// The XAML compiler emits that list into this assembly as a private member of
/// <c>&lt;App&gt;_XamlTypeInfo.XamlTypeInfoProvider</c>, populated from the assemblies referenced at
/// build time — which a plugin, by definition, is not.
/// <para>
/// Reflection rather than a part of the same partial class: the file holding those members is
/// written by the XAML compiler's second pass, after the C# that would name them has already been
/// compiled, so a compile-time reference does not exist to be written. Every failure is returned,
/// never thrown: a plugin that declares no XAML types of its own never needs this.
/// </para>
/// </remarks>
internal static class XamlMetadataBridge
{
    private const BindingFlags _instance = BindingFlags.Instance | BindingFlags.NonPublic;

    private const string _appProviderMember = "_AppProvider";

    private const string _typeInfoProviderMember = "Provider";

    private const string _otherProvidersMember = "OtherProviders";

    public static Result Add(Application application, IXamlMetadataProvider provider)
    {
        var appProvider = ReadProperty(application, _appProviderMember);

        if (appProvider is null)
        {
            return Result.Failure(PluginXamlErrors.MetadataBridgeUnavailable(_appProviderMember));
        }

        var typeInfoProvider = ReadProperty(appProvider, _typeInfoProviderMember);

        if (typeInfoProvider is null)
        {
            return Result.Failure(PluginXamlErrors.MetadataBridgeUnavailable(_typeInfoProviderMember));
        }

        if (ReadProperty(typeInfoProvider, _otherProvidersMember) is not IList<IXamlMetadataProvider> others)
        {
            return Result.Failure(PluginXamlErrors.MetadataBridgeUnavailable(_otherProvidersMember));
        }

        try
        {
            others.Add(provider);
        }
        catch (NotSupportedException)
        {
            // A generated list that is fixed-size or read-only would take the provider nowhere.
            return Result.Failure(PluginXamlErrors.MetadataBridgeUnavailable(_otherProvidersMember));
        }

        return Result.Success();
    }

    /// <summary>
    /// The generated provider a plugin assembly carries, or null when it declares no XAML types.
    /// </summary>
    /// <remarks>
    /// Found by interface rather than by name: the generated type's namespace is derived from the
    /// assembly's own name, and deriving it back would be a second place to get that spelling right.
    /// </remarks>
    public static IXamlMetadataProvider? FindProvider(Assembly assembly, out string reason)
    {
        reason = string.Empty;
        Type[] candidates;

        try
        {
            candidates = assembly.GetExportedTypes();
        }
        catch (Exception exception)
        {
            // Reading a plugin's types resolves what it references, so a missing dependency
            // surfaces here rather than as a type nobody asked for.
            reason = $"its types could not be read: {exception.Message}";

            return null;
        }

        foreach (var candidate in candidates)
        {
            if (candidate.IsAbstract
                || candidate.IsGenericTypeDefinition
                || !typeof(IXamlMetadataProvider).IsAssignableFrom(candidate))
            {
                continue;
            }

            if (candidate.GetConstructor(System.Type.EmptyTypes) is null)
            {
                reason = $"'{candidate.FullName}' has no parameterless constructor";

                continue;
            }

            try
            {
                return (IXamlMetadataProvider)System.Activator.CreateInstance(candidate)!;
            }
            catch (Exception exception)
            {
                // The plugin's own constructor. It runs before anything else of the plugin's does,
                // so an exception here would be the first thing a plugin could stop a launch with.
                reason = $"'{candidate.FullName}' threw {exception.GetType().Name}: {exception.Message}";

                return null;
            }
        }

        return null;
    }

    private static object? ReadProperty(object target, string name)
    {
        return target
            .GetType()
            .GetProperty(name, _instance)?
            .GetValue(target);
    }
}

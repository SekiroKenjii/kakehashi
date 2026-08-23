using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using __ROOT_NAMESPACE__.SharedKernel;

namespace __ROOT_NAMESPACE__.PluginSdk.Abstractions;

/// <summary>
/// What one assembly says about itself, read without loading it.
/// </summary>
/// <remarks>
/// Metadata rather than reflection, and the difference is the whole point: answering these
/// questions by loading the assembly would mean resolving everything it references — the framework
/// included — and running its module initializers, on a machine that has not agreed to run it yet.
/// A metadata reader compares names as strings, so it needs nothing but the file.
/// <para>
/// The cost of that is stated where each member is: a name comparison sees what a type declares,
/// never what it inherits through something else.
/// </para>
/// </remarks>
public sealed class PluginAssembly
{
    /// <summary>The framework base every compiled XAML page derives from.</summary>
    private const string _pageBaseType = "Microsoft.UI.Xaml.Controls.Page";

    /// <summary>
    /// What the XAML compiler emits into an assembly that carries compiled markup, and what the
    /// host's bridge looks for to decide the same thing at load.
    /// </summary>
    private const string _xamlMetadataProvider = "Microsoft.UI.Xaml.Markup.IXamlMetadataProvider";

    private const string _moduleInterface = "__ROOT_NAMESPACE__.UI.Contracts.IModule";

    private readonly HashSet<string> _types;
    private readonly HashSet<string> _modules;

    private PluginAssembly(
        HashSet<string> types, HashSet<string> modules, IReadOnlyList<string> pageTypes, bool declaresXaml)
    {
        _types = types;
        _modules = modules;
        PageTypes = pageTypes;
        DeclaresXaml = declaresXaml;
    }

    /// <summary>
    /// Whether it carries compiled XAML, which is what makes it need a resource index beside it.
    /// </summary>
    public bool DeclaresXaml { get; }

    /// <summary>Every direct subclass of the framework's page type, by full name.</summary>
    /// <remarks>
    /// Direct, because a base type is a name in this assembly's metadata and following it further
    /// would mean resolving the assembly that declares it. The XAML compiler emits a direct
    /// subclass for every markup page, so what this misses is a page written by hand on top of
    /// somebody's own base class.
    /// </remarks>
    public IReadOnlyList<string> PageTypes { get; }

    /// <summary>Reads one assembly. A file that is not a managed assembly is a refusal, not a throw.</summary>
    public static Result<PluginAssembly> Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        try
        {
            using var reader = new PEReader(stream, PEStreamOptions.LeaveOpen);

            return reader.HasMetadata
                ? Result.Success(From(reader.GetMetadataReader()))
                : Result.Failure<PluginAssembly>(PluginErrors.AssemblyUnreadable);
        }
        catch (Exception exception) when (exception is BadImageFormatException or InvalidOperationException)
        {
            return Result.Failure<PluginAssembly>(PluginErrors.AssemblyUnreadable);
        }
    }

    /// <summary>Whether this assembly declares that type at all.</summary>
    public bool Declares(string typeName) => _types.Contains(typeName);

    /// <summary>Whether that type is one the host could mount as a module.</summary>
    public bool DeclaresModule(string typeName) => _modules.Contains(typeName);

    private static PluginAssembly From(MetadataReader metadata)
    {
        var types = new HashSet<string>(StringComparer.Ordinal);
        var modules = new HashSet<string>(StringComparer.Ordinal);
        var pages = new List<string>();
        var declaresXaml = false;

        foreach (var handle in metadata.TypeDefinitions)
        {
            var type = metadata.GetTypeDefinition(handle);
            var name = FullName(metadata.GetString(type.Namespace), metadata.GetString(type.Name));
            types.Add(name);

            if (BaseTypeName(metadata, type) == _pageBaseType)
            {
                pages.Add(name);
            }

            foreach (var interfaceName in InterfaceNames(metadata, type))
            {
                declaresXaml = declaresXaml || interfaceName == _xamlMetadataProvider;

                if (interfaceName == _moduleInterface)
                {
                    modules.Add(name);
                }
            }
        }

        return new PluginAssembly(types, modules, pages, declaresXaml);
    }

    private static string FullName(string nameSpace, string name)
    {
        return nameSpace.Length == 0 ? name : nameSpace + "." + name;
    }

    /// <summary>The base type's name, or none where it is not a name this assembly can state.</summary>
    /// <remarks>
    /// A nil handle is what <c>System.Object</c> and the synthetic module type carry, and reading
    /// it as a row index walks off the end of the table — so the check comes first.
    /// </remarks>
    private static string BaseTypeName(MetadataReader metadata, TypeDefinition type)
    {
        return type.BaseType.IsNil ? string.Empty : HandleName(metadata, type.BaseType);
    }

    private static IEnumerable<string> InterfaceNames(MetadataReader metadata, TypeDefinition type)
    {
        foreach (var handle in type.GetInterfaceImplementations())
        {
            var implemented = metadata.GetInterfaceImplementation(handle).Interface;

            if (!implemented.IsNil)
            {
                yield return HandleName(metadata, implemented);
            }
        }
    }

    /// <summary>
    /// The name behind a type handle, whichever table it points into.
    /// </summary>
    /// <remarks>
    /// Both cases are reachable and neither is theoretical: a plugin names the framework's types
    /// through references, and an assembly that declares its own base class names it through a
    /// definition. Dropping either branch silently halves what this sees.
    /// </remarks>
    private static string HandleName(MetadataReader metadata, EntityHandle handle)
    {
        if (handle.Kind == HandleKind.TypeReference)
        {
            var reference = metadata.GetTypeReference((TypeReferenceHandle)handle);

            return FullName(metadata.GetString(reference.Namespace), metadata.GetString(reference.Name));
        }

        if (handle.Kind == HandleKind.TypeDefinition)
        {
            var definition = metadata.GetTypeDefinition((TypeDefinitionHandle)handle);

            return FullName(metadata.GetString(definition.Namespace), metadata.GetString(definition.Name));
        }

        // A TypeSpecification: a generic instantiation, which none of these questions is about.
        return string.Empty;
    }
}

namespace Microsoft.UI.Xaml.Controls;

/// <summary>
/// A stand-in for the framework's page type, so this assembly's metadata carries its full name.
/// </summary>
/// <remarks>
/// PluginAssembly compares names as strings, so a fixture under any other namespace would prove
/// nothing — and referencing the real Windows App SDK would make a plain net10.0 test project a
/// Windows one in order to assert something about text.
/// <para>
/// These stand-ins are also why the reader resolves a base or interface handle from the definition
/// table as well as the reference table: here they are definitions, and in a real plugin they are
/// references. Removing that branch would silently stop this fixture from proving anything.
/// </para>
/// </remarks>
public class Page;

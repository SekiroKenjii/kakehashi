namespace __ROOT_NAMESPACE__.ArchitectureTests;

/// <summary>
/// The assembly-name root, named once. Every assertion in this project that matches on an assembly
/// name builds its string from here.
/// </summary>
/// <remarks>
/// Assembly names are data to these tests, not code, so a rename of the application would otherwise
/// have to find them one literal at a time. This is the single place a scaffold substitutes. It
/// tracks the assembly name rather than the root namespace: the two are the same by default and a
/// scaffold may set them apart, and what GetReferencedAssemblies reports is the assembly.
/// </remarks>
internal static class TestConstants
{
    public const string AssemblyRoot = "__APP_NAME__";
    public const string AssemblyPrefix = AssemblyRoot + ".";
    public const string ModulesPrefix = AssemblyPrefix + "Modules.";
    public const string ContractsAssembly = AssemblyPrefix + "Contracts";
    public const string MediatorAssembly = AssemblyPrefix + "Mediator";
}

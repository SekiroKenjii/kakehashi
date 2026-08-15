namespace Kakehashi.ArchitectureTests;

/// <summary>
/// The root namespace, named once. Every assertion in this project that matches on an assembly name
/// builds its string from here.
/// </summary>
/// <remarks>
/// Assembly names are data to these tests, not code, so a rename of the application would otherwise
/// have to find them one literal at a time. This is the single place a scaffold substitutes.
/// </remarks>
internal static class TestConstants
{
    public const string RootNamespace = "Kakehashi";
    public const string RootPrefix = RootNamespace + ".";
    public const string ModulesPrefix = RootPrefix + "Modules.";
    public const string ContractsAssembly = RootPrefix + "Contracts";
    public const string MediatorAssembly = RootPrefix + "Mediator";
}
